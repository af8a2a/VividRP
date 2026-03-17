using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class DeferredDirectionalLightingComputeTests
    {
        [Test]
        public void DeferredDirectionalLightingCompute_DeclaresExpectedKernelsAndClassificationInputs()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#pragma kernel ClearDeferredLit"));
            Assert.That(source, Does.Contain("#pragma kernel DeferredLit"));
            Assert.That(source, Does.Contain("_GBuffer0"));
            Assert.That(source, Does.Contain("_GBuffer1"));
            Assert.That(source, Does.Contain("_GBuffer2"));
            Assert.That(source, Does.Contain("_GBuffer3"));
            Assert.That(source, Does.Contain("_DepthTexture"));
            Assert.That(source, Does.Contain("_MaterialPixelIndices"));
            Assert.That(source, Does.Contain("_MaterialDispatchArgs"));
            Assert.That(source, Does.Contain("_LightingTexture"));
            Assert.That(source, Does.Contain("#define CLASSIFY_TILE_SIZE 8"));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/TileClassification.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/LightingLoop.hlsl\""));
            Assert.That(source, Does.Contain("_DirectionalLightCount"));
            Assert.That(source, Does.Contain("HasPunctualLights()"));
            Assert.That(source, Does.Contain("VividLightingLoop::Create"));
            Assert.That(source, Does.Contain("VividLightingLoop::GetPunctualLightCount"));
            Assert.That(source, Does.Contain("VividLightingLoop::LoadPunctualLight"));
            Assert.That(source, Does.Contain("EvaluateDeferredLitLighting"));
            Assert.That(source, Does.Contain("EvaluateIndirectLighting"));
            Assert.That(source, Does.Contain("EvaluatePunctualLight"));
            Assert.That(source, Does.Contain("ComputeWorldSpacePosition"));
            Assert.That(source, Does.Contain("_MaterialDispatchArgs[1]"));
            Assert.That(source, Does.Contain("UnpackTileCoord"));
            Assert.That(source, Does.Contain("tileCoord * CLASSIFY_TILE_SIZE"));
        }

        [Test]
        public void VividRPCoreResources_DeclaresDeferredLitCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.DeferredLitCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Material/DeferredLit"));
        }

        private static string GetComputeShaderSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Material", "DeferredLit.compute");

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
