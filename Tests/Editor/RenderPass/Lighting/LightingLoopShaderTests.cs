using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class LightingLoopShaderTests
    {
        [Test]
        public void LightingLoopShader_SeparatesLightDataFromClusterLookup()
        {
            string lightingSource = File.ReadAllText(GetPublicShaderPath("Lighting.hlsl"));
            string clusteredLightingSource = File.ReadAllText(GetPublicShaderPath("ClusteredLighting.hlsl"));
            string lightingLoopSource = File.ReadAllText(GetPublicShaderPath("LightingLoop.hlsl"));

            Assert.That(lightingSource, Does.Contain("StructuredBuffer<PunctualLightData> _PunctualLights;"));
            Assert.That(lightingSource, Does.Contain("StructuredBuffer<AreaLightData> _AreaLights;"));
            Assert.That(lightingSource, Does.Contain("StructuredBuffer<ReflectionProbeData> _ReflectionProbes;"));
            Assert.That(lightingSource, Does.Contain("TEXTURE2D_ARRAY(_ReflectionAtlas);"));
            Assert.That(lightingSource, Does.Contain("_ReflectionAtlasCubeData"));
            Assert.That(lightingSource, Does.Contain("_ReflectionAtlasMipCount"));
            Assert.That(lightingSource, Does.Contain("_ReflectionAtlasSliceCount"));
            Assert.That(lightingSource, Does.Contain("GetReflectionProbe"));
            Assert.That(lightingSource, Does.Contain("capturePositionWS"));
            Assert.That(lightingSource, Does.Contain("IsReflectionProbeAtlasEntryValid"));
            Assert.That(lightingSource, Does.Contain("EvaluateReflectionProbeInfluenceWeight"));
            Assert.That(lightingSource, Does.Contain("ProjectReflectionProbeDirection"));
            Assert.That(lightingSource, Does.Contain("GetReflectionProbeAtlasCoords"));
            Assert.That(lightingSource, Does.Contain("SampleReflectionProbeAtlas"));
            Assert.That(lightingSource, Does.Contain("TryEvaluateReflectionProbeData"));
            Assert.That(lightingSource, Does.Contain("PerceptualRoughnessToMipmapLevel"));
            Assert.That(lightingSource, Does.Contain("PackNormalOctQuadEncode"));
            Assert.That(lightingSource, Does.Not.Contain("_PunctualLightCount"));
            Assert.That(lightingSource, Does.Not.Contain("_AreaLightCount"));
            Assert.That(lightingSource, Does.Not.Contain("ClusteredLighting.hlsl"));

            Assert.That(clusteredLightingSource, Does.Contain("struct VividClusteredLightCell"));
            Assert.That(clusteredLightingSource, Does.Contain("_ClusteredPunctualLightGridEnabled"));
            Assert.That(clusteredLightingSource, Does.Contain("_ClusteredAreaLightGridEnabled"));
            Assert.That(clusteredLightingSource, Does.Contain("_ClusteredReflectionProbeGridEnabled"));
            Assert.That(clusteredLightingSource, Does.Contain("LoadPunctualLightCell"));
            Assert.That(clusteredLightingSource, Does.Contain("LoadAreaLightCell"));
            Assert.That(clusteredLightingSource, Does.Contain("LoadReflectionProbeCell"));
            Assert.That(clusteredLightingSource, Does.Contain("struct VividBigTileLightCell"));
            Assert.That(clusteredLightingSource, Does.Contain("StructuredBuffer<uint> g_vBigTileLightList"));
            Assert.That(clusteredLightingSource, Does.Contain("LoadBigTileLightCell"));
            Assert.That(clusteredLightingSource, Does.Contain("LoadBigTileLightIndex"));

            Assert.That(lightingLoopSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl\""));
            Assert.That(lightingLoopSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/ClusteredLighting.hlsl\""));
            Assert.That(lightingLoopSource, Does.Contain("struct VividLightingLoop"));
            Assert.That(lightingLoopSource, Does.Contain("struct VividBigTileLightingLoopContext"));
            Assert.That(lightingLoopSource, Does.Contain("GetPunctualLightCount"));
            Assert.That(lightingLoopSource, Does.Contain("LoadPunctualLight"));
            Assert.That(lightingLoopSource, Does.Contain("GetAreaLightCount"));
            Assert.That(lightingLoopSource, Does.Contain("LoadAreaLight"));
            Assert.That(lightingLoopSource, Does.Contain("GetReflectionProbeCount"));
            Assert.That(lightingLoopSource, Does.Contain("LoadReflectionProbe"));
            Assert.That(lightingLoopSource, Does.Contain("TryLoadReflectionProbe"));
            Assert.That(lightingLoopSource, Does.Contain("TryEvaluateReflectionProbe"));
            Assert.That(lightingLoopSource, Does.Contain("TryEvaluateReflectionProbes"));
            Assert.That(lightingLoopSource, Does.Contain("CreateBigTile"));
            Assert.That(lightingLoopSource, Does.Contain("GetBigTileLightCount"));
            Assert.That(lightingLoopSource, Does.Contain("GetBigTileLightIndex"));
            Assert.That(lightingLoopSource, Does.Contain("LoadBigTilePunctualLight"));
            Assert.That(lightingLoopSource, Does.Contain("LoadBigTileAreaLight"));
            Assert.That(lightingLoopSource, Does.Contain("TryGetBigTileReflectionProbeIndex"));
            Assert.That(lightingLoopSource, Does.Contain("LoadBigTileReflectionProbe"));
            Assert.That(lightingLoopSource, Does.Contain("TryLoadBigTileReflectionProbe"));
            Assert.That(lightingLoopSource, Does.Contain("TryEvaluateBigTileReflectionProbe"));
            Assert.That(lightingLoopSource, Does.Contain("TryEvaluateBigTileReflectionProbes"));
            Assert.That(lightingLoopSource, Does.Contain("GetBigTileDecalIndex"));
            Assert.That(lightingLoopSource, Does.Contain("GetBigTilePunctualLightCount"));
            Assert.That(lightingLoopSource, Does.Contain("GetBigTileAreaLightCount"));
            Assert.That(lightingLoopSource, Does.Contain("GetBigTileReflectionProbeCount"));
            Assert.That(lightingLoopSource, Does.Contain("GetBigTileDecalCount"));
            Assert.That(lightingLoopSource, Does.Not.Contain("HasPunctualLights"));
            Assert.That(lightingLoopSource, Does.Not.Contain("HasAreaLights"));
        }

        private static string GetPublicShaderPath(string fileName)
        {
            string shaderPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Core",
                "Public",
                fileName));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");
            return shaderPath;
        }
    }
}
