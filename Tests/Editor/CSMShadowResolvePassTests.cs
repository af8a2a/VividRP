using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class CSMShadowResolvePassTests
    {
        [Test]
        public void CSMShadowResolvePass_UploadsPerLightScreenSpaceShadowQuality()
        {
            var source = File.ReadAllText(GetPassSourcePath());

            Assert.That(source, Does.Contain("private static readonly int CSMShadowQualityId = Shader.PropertyToID(\"_CSMShadowQuality\");"));
            Assert.That(source, Does.Contain("private static readonly int CSMLightAngularDiameterId = Shader.PropertyToID(\"_CSMLightAngularDiameter\");"));
            Assert.That(source, Does.Contain("m_ShadowQuality = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low;"));
            Assert.That(source, Does.Contain("CoreUtils.DivRoundUp(width, ThreadGroupSizeX);"));
            Assert.That(source, Does.Contain("TryResolveMainDirectionalLight(lightData, out _, out var additionalLightData)"));
            Assert.That(source, Does.Contain("m_ShadowQuality = (int)additionalLightData.screenSpaceShadowQuality;"));
            Assert.That(source, Does.Contain("m_LightAngularDiameter = Mathf.Max(additionalLightData.resolvedAngularDiameter, 0.0f);"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ResolveCompute, CSMShadowQualityId, m_ShadowQuality);"));
            Assert.That(source, Does.Contain("cmd.SetComputeFloatParam(m_ResolveCompute, CSMLightAngularDiameterId, m_LightAngularDiameter);"));
        }

        private static string GetPassSourcePath()
        {
            var passPath = GetPackageFilePath("Runtime", "RenderPass", "Core", "CSMShadowResolvePass.cs");

            Assert.That(File.Exists(passPath), Is.True, $"Expected pass source at '{passPath}'.");
            return passPath;
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
