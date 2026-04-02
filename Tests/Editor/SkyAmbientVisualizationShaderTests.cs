using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public class SkyAmbientVisualizationShaderTests
    {
        [Test]
        public void Shader_UsesVividForwardPassAndSamplesSkySphericalHarmonics()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "SkyAmbientVisualization.shader"));

            Assert.That(source, Does.Contain("Shader \"VividRP/Material/SkyAmbientVisualization\""));
            Assert.That(source, Does.Contain("#pragma target 4.5"));
            Assert.That(source, Does.Contain("Name \"VividForward\""));
            Assert.That(source, Does.Contain("\"LightMode\" = \"VividForward\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/BakedGI.hlsl\""));
            Assert.That(source, Does.Contain("float3 ambientLighting = max(VividSampleAmbientProbe(normalWS), 0.0);"));
            Assert.That(source, Does.Contain("return half4(VividApplyPreExposure(ambientLighting), 1.0);"));
            Assert.That(source, Does.Contain("output.normalWS = TransformObjectToWorldNormal(input.normalOS);"));
        }

        [Test]
        public void SimpleForwardShader_AppliesPreExposureToForwardOutput()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "SimpleForward.shader"));

            Assert.That(source, Does.Contain("Shader \"VividRP/Material/SimpleForward\""));
            Assert.That(source, Does.Contain("#pragma target 3.5"));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl\""));
            Assert.That(source, Does.Contain("surfaceColor.rgb = VividApplyPreExposure(surfaceColor.rgb);"));
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
