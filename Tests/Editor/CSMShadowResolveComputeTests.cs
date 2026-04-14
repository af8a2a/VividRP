using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class CSMShadowResolveComputeTests
    {
        [Test]
        public void CSMShadowResolveCompute_UsesSharedWorldPositionAndNormalDecodingHelpers()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl\""));
            Assert.That(source, Does.Contain("return ComputeWorldSpacePosition(uv, deviceDepth, _CSMInvViewProjMatrix);"));
            Assert.That(source, Does.Contain("float3 normalWS = DecodeVividNormalOct(gbuffer1.xy);"));
            Assert.That(source, Does.Not.Contain("normalize(gbuffer1.xyz * 2.0 - 1.0)"));
        }

        private static string GetComputeShaderSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Core", "Private", "CSMShadowResolve.compute");

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected compute shader source at '{shaderPath}'.");
            return shaderPath;
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
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
