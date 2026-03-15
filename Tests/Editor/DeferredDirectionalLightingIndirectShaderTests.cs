using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class DeferredDirectionalLightingIndirectShaderTests
    {
        [Test]
        public void DeferredDirectionalLightingIndirectShader_DeclaresIndirectPixelLightingInputs()
        {
            var shaderSource = File.ReadAllText(GetShaderSourcePath());
            var hlslSource = File.ReadAllText(GetPassSourcePath());
            var passSource = File.ReadAllText(GetRenderPassSourcePath());

            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/DeferredDirectionalLightingIndirect\""));
            Assert.That(hlslSource, Does.Contain("StructuredBuffer<uint> _MaterialPixelIndices;"));
            Assert.That(hlslSource, Does.Contain("_LightingWidth"));
            Assert.That(hlslSource, Does.Contain("_LightingHeight"));
            Assert.That(hlslSource, Does.Contain("_DirectionalLightCount"));
            Assert.That(hlslSource, Does.Contain("_PunctualLightCount"));
            Assert.That(hlslSource, Does.Contain("GetClusterLightIndex"));
            Assert.That(hlslSource, Does.Contain("EvaluateDeferredDirectionalLighting"));
            Assert.That(hlslSource, Does.Contain("EvaluatePunctualLight"));
            Assert.That(hlslSource, Does.Contain("ComputeWorldSpacePosition"));
            Assert.That(passSource, Does.Contain("DrawProceduralIndirect"));
            Assert.That(passSource, Does.Contain("MeshTopology.Points"));
        }

        [Test]
        public void VividRPCoreResources_DeclaresDeferredDirectionalLightingIndirectShader()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.DeferredDirectionalLightingIndirectShader));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Material/DeferredDirectionalLightingIndirect"));
        }

        [Test]
        public void DeferredDirectionalLightingPass_UsesExpectedFallbackShaderName()
        {
            var passSource = File.ReadAllText(GetRenderPassSourcePath());

            Assert.That(passSource, Does.Contain("DeferredDirectionalLightingIndirectShaderName = \"Hidden/VividRP/DeferredDirectionalLightingIndirect\""));
        }

        private static string GetShaderSourcePath()
        {
            var shaderPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Material",
                "DeferredDirectionalLightingIndirect.shader"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");
            return shaderPath;
        }

        private static string GetPassSourcePath()
        {
            var passPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Material",
                "DeferredDirectionalLightingIndirectPass.hlsl"));

            Assert.That(File.Exists(passPath), Is.True, $"Expected shader pass source at '{passPath}'.");
            return passPath;
        }

        private static string GetRenderPassSourcePath()
        {
            var passPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Runtime",
                "RenderPass",
                "Core",
                "DeferredDirectionalLightingPass.cs"));

            Assert.That(File.Exists(passPath), Is.True, $"Expected render pass source at '{passPath}'.");
            return passPath;
        }
    }
}
