using System.Reflection;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class DeferredDirectionalLightingComputeTests
    {
        [Test]
        public void DeferredDirectionalLightingCompute_DeclaresExpectedKernelAndCoreInputs()
        {
            string source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#pragma kernel DeferredDirectionalLighting"));
            Assert.That(source, Does.Contain("_GBuffer0"));
            Assert.That(source, Does.Contain("_GBuffer1"));
            Assert.That(source, Does.Contain("_GBuffer2"));
            Assert.That(source, Does.Contain("_GBuffer3"));
            Assert.That(source, Does.Contain("_DepthTexture"));
            Assert.That(source, Does.Contain("_LightingTexture"));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl\""));
            Assert.That(source, Does.Contain("_DirectionalLightCount"));
            Assert.That(source, Does.Contain("EvaluateDeferredDirectionalLighting"));
            Assert.That(source, Does.Contain("ComputeWorldSpacePosition"));
        }

        [Test]
        public void VividRPCoreResources_DeclaresDeferredDirectionalLightingCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.DeferredDirectionalLightingCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Material/DeferredDirectionalLighting"));
        }

        private static string GetComputeShaderSourcePath()
        {
            string shaderPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Material",
                "DeferredDirectionalLighting.compute"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected compute shader source at '{shaderPath}'.");
            return shaderPath;
        }
    }
}
