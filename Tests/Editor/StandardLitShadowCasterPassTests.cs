using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class StandardLitShadowCasterPassTests
    {
        [Test]
        public void StandardLitShadowCasterPass_AppliesCasterBiasAndNearClipClamping()
        {
            var source = File.ReadAllText(GetSourcePath());

            Assert.That(source, Does.Contain("float4 _ShadowBias;"));
            Assert.That(source, Does.Contain("float3 _LightDirection;"));
            Assert.That(source, Does.Contain("float3 _LightPosition;"));
            Assert.That(source, Does.Contain("float3 normalOS : NORMAL;"));
            Assert.That(source, Does.Contain("float3 ApplyVividShadowBias(float3 positionWS, float3 normalWS, float3 lightDirectionWS)"));
            Assert.That(source, Does.Contain("float4 ApplyVividShadowClamping(float4 positionCS)"));
            Assert.That(source, Does.Contain("float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);"));
            Assert.That(source, Does.Contain("float3 normalWS = TransformObjectToWorldNormal(input.normalOS);"));
            Assert.That(source, Does.Contain("output.positionCS = TransformWorldToHClip(ApplyVividShadowBias(positionWS, normalWS, lightDirectionWS));"));
            Assert.That(source, Does.Contain("output.positionCS = ApplyVividShadowClamping(output.positionCS);"));
        }

        private static string GetSourcePath()
        {
            var sourcePath = GetPackageFilePath("Shaders", "Material", "ShaderPass", "StandardLitShadowCasterPass.hlsl");

            Assert.That(File.Exists(sourcePath), Is.True, $"Expected source at '{sourcePath}'.");
            return sourcePath;
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
