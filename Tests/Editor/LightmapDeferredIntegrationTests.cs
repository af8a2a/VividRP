using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class LightmapDeferredIntegrationTests
    {
        [Test]
        public void LightmapAwareGBufferShaders_DeclareLightmapVariantsAndSamplingHelpers()
        {
            var standardLitShader = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "StandardLit.shader"));
            var simpleLitShader = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "SimpleLit.shader"));
            var standardLitPass = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "StandardLitGBufferPass.hlsl"));
            var simpleLitPass = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "SimpleLitGBufferPass.hlsl"));
            var bakedGiSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "BakedGI.hlsl"));

            Assert.That(standardLitShader, Does.Contain("#pragma multi_compile _ LIGHTMAP_ON"));
            Assert.That(standardLitShader, Does.Contain("#pragma multi_compile _ DIRLIGHTMAP_COMBINED"));
            Assert.That(simpleLitShader, Does.Contain("#pragma multi_compile _ LIGHTMAP_ON"));
            Assert.That(simpleLitShader, Does.Contain("#pragma multi_compile _ DIRLIGHTMAP_COMBINED"));
            Assert.That(standardLitPass, Does.Contain("SampleStandardLitBakedGI(input.lightmapUV, surfaceData.normalWS, input.positionWS)"));
            Assert.That(standardLitPass, Does.Contain("SampleVividProbeVolume("));
            Assert.That(simpleLitPass, Does.Contain("SampleVividBakedGI(input.lightmapUV, surfaceData.normalWS)"));
            Assert.That(bakedGiSource, Does.Contain("SampleSingleLightmap("));
            Assert.That(bakedGiSource, Does.Contain("SampleDirectionalLightmap("));
            Assert.That(bakedGiSource, Does.Contain("unity_LightmapST"));
        }

        [Test]
        public void DeferredIndirectLighting_UsesBakedGiWhenGBufferProvidesIt()
        {
            var hdrpLightingSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "HdrpLitLighting.hlsl"));
            var deferredComputeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredLit.compute"));

            Assert.That(hdrpLightingSource, Does.Contain("surfaceData.hasBakedGI > 0.5"));
            Assert.That(hdrpLightingSource, Does.Contain("? surfaceData.bakedGI"));
            Assert.That(deferredComputeSource, Does.Contain("Texture2D<float4> _GBuffer4;"));
            Assert.That(deferredComputeSource, Does.Contain("float4 rt4 = _GBuffer4.Load(int3(pixelCoord, 0));"));
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
