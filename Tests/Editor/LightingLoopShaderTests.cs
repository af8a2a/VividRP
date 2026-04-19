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
            Assert.That(lightingSource, Does.Not.Contain("ClusteredLighting.hlsl"));

            Assert.That(clusteredLightingSource, Does.Contain("struct VividClusteredLightCell"));
            Assert.That(clusteredLightingSource, Does.Contain("LoadPunctualLightCell"));
            Assert.That(clusteredLightingSource, Does.Contain("LoadAreaLightCell"));

            Assert.That(lightingLoopSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl\""));
            Assert.That(lightingLoopSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/ClusteredLighting.hlsl\""));
            Assert.That(lightingLoopSource, Does.Contain("struct VividLightingLoop"));
            Assert.That(lightingLoopSource, Does.Contain("LoadPunctualLight"));
            Assert.That(lightingLoopSource, Does.Contain("GetAreaLightCount"));
            Assert.That(lightingLoopSource, Does.Contain("LoadAreaLight"));
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
