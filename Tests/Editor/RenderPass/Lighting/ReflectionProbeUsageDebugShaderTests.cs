using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class ReflectionProbeUsageDebugShaderTests
    {
        [Test]
        public void Shader_VisualizesReflectionProbeUsage_FromForwardMaterial()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Debug", "ReflectionProbeUsageDebug.shader"));

            Assert.That(source, Does.Contain("Shader \"VividRP/Material/ReflectionProbeUsageDebug\""));
            Assert.That(source, Does.Contain("#pragma target 4.5"));
            Assert.That(source, Does.Contain("Name \"VividForward\""));
            Assert.That(source, Does.Contain("\"LightMode\" = \"VividForward\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/LightingLoop.hlsl\""));
            Assert.That(source, Does.Contain("VividLightingLoop::Create(pixelCoord, input.positionWS)"));
            Assert.That(source, Does.Contain("VividLightingLoop::TryEvaluateReflectionProbes"));
            Assert.That(source, Does.Contain("GetReflectionProbeAtlasMipLevel"));
            Assert.That(source, Does.Contain("VIVID_REFLECTION_PROBE_USAGE_DEBUG_WEIGHTED_RADIANCE"));
            Assert.That(source, Does.Contain("VIVID_REFLECTION_PROBE_USAGE_DEBUG_AVERAGE_RADIANCE"));
            Assert.That(source, Does.Contain("VIVID_REFLECTION_PROBE_USAGE_DEBUG_WEIGHT"));
            Assert.That(source, Does.Contain("VIVID_REFLECTION_PROBE_USAGE_DEBUG_MIP_LEVEL"));
            Assert.That(source, Does.Contain("VIVID_REFLECTION_PROBE_USAGE_DEBUG_REFLECTION_DIRECTION"));
            Assert.That(source, Does.Contain("VIVID_REFLECTION_PROBE_USAGE_DEBUG_PROBE_COUNT"));
            Assert.That(source, Does.Contain("return half4(VividApplyPreExposure(ApplyDebugDisplayScale(debugColor)), 1.0);"));
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
