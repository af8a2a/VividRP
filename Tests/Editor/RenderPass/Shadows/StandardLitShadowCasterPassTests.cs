using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class StandardLitShadowCasterPassTests
    {
        [Test]
        public void StandardLitShadowCasterPass_UsesSharedMeshPassAndAppliesNearClipClamping()
        {
            var wrapperSource = File.ReadAllText(GetStandardLitShadowCasterSourcePath());
            var sharedPassSource = File.ReadAllText(GetSharedShadowCasterSourcePath());
            var vertMeshSource = File.ReadAllText(GetVividVertMeshSourcePath());

            Assert.That(wrapperSource, Does.Contain("StandardLitInput.hlsl"));
            Assert.That(wrapperSource, Does.Contain("VividShaderPassShadowCaster.hlsl"));
            Assert.That(wrapperSource, Does.Not.Contain("struct Attributes"));
            Assert.That(wrapperSource, Does.Not.Contain("struct Varyings"));

            Assert.That(sharedPassSource, Does.Contain("float4 _ShadowBias;"));
            Assert.That(sharedPassSource, Does.Not.Contain("float3 _LightDirection;"));
            Assert.That(sharedPassSource, Does.Not.Contain("float3 _LightPosition;"));
            Assert.That(sharedPassSource, Does.Not.Contain("float3 ApplyVividShadowBias(float3 positionWS, float3 lightDirectionWS)"));
            Assert.That(sharedPassSource, Does.Contain("float4 ApplyVividShadowClamping(float4 positionCS)"));
            Assert.That(sharedPassSource, Does.Contain("VividVaryingsMesh output = VividVertMesh(input);"));
            Assert.That(sharedPassSource, Does.Contain("output.positionCS = ApplyVividShadowClamping(output.positionCS);"));

            Assert.That(vertMeshSource, Does.Contain("float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);"));
            Assert.That(vertMeshSource, Does.Contain("output.positionCS = TransformWorldToHClip(positionWS);"));
            Assert.That(vertMeshSource, Does.Not.Contain("float normalOffsetScale = invNdotL * _ShadowBias.y;"));
            Assert.That(vertMeshSource, Does.Not.Contain("return positionWS + lightDirectionWS * _ShadowBias.x;"));
        }

        private static string GetStandardLitShadowCasterSourcePath()
        {
            var sourcePath = GetPackageFilePath("Shaders", "Material", "StandardLit", "StandardLitShadowCasterPass.hlsl");

            Assert.That(File.Exists(sourcePath), Is.True, $"Expected source at '{sourcePath}'.");
            return sourcePath;
        }

        private static string GetSharedShadowCasterSourcePath()
        {
            var sourcePath = GetPackageFilePath("Shaders", "Material", "ShaderPass", "VividShaderPassShadowCaster.hlsl");

            Assert.That(File.Exists(sourcePath), Is.True, $"Expected source at '{sourcePath}'.");
            return sourcePath;
        }

        private static string GetVividVertMeshSourcePath()
        {
            var sourcePath = GetPackageFilePath("Shaders", "Material", "ShaderPass", "VividVertMesh.hlsl");

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
