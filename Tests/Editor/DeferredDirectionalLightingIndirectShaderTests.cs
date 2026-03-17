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

            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/DeferredDirectionalLightingIndirect\""));
            Assert.That(hlslSource, Does.Contain("StructuredBuffer<uint> _MaterialPixelIndices;"));
            Assert.That(hlslSource, Does.Contain("_LightingWidth"));
            Assert.That(hlslSource, Does.Contain("_LightingHeight"));
            Assert.That(hlslSource, Does.Contain("_DirectionalLightCount"));
            Assert.That(hlslSource, Does.Contain("HasPunctualLights()"));
            Assert.That(hlslSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/LightingLoop.hlsl\""));
            Assert.That(hlslSource, Does.Contain("VividLightingLoop::Create"));
            Assert.That(hlslSource, Does.Contain("VividLightingLoop::GetPunctualLightCount"));
            Assert.That(hlslSource, Does.Contain("VividLightingLoop::LoadPunctualLight"));
            Assert.That(hlslSource, Does.Contain("EvaluateDeferredDirectionalLighting"));
            Assert.That(hlslSource, Does.Contain("EvaluateIndirectLighting"));
            Assert.That(hlslSource, Does.Contain("EvaluatePunctualLight"));
            Assert.That(hlslSource, Does.Contain("ComputeWorldSpacePosition"));
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
        public void DeferredDirectionalLightingPass_UsesUnsafeComputeDispatchAndConsumesPreparedIblResources()
        {
            var passSource = File.ReadAllText(GetRenderPassSourcePath());

            Assert.That(passSource, Does.Contain("DeferredLitCompute"));
            Assert.That(passSource, Does.Contain("VividPreIntegratedFGD"));
            Assert.That(passSource, Does.Contain("GetNativeCommandBuffer"));
            Assert.That(passSource, Does.Contain("PreparePreIntegratedFGDResources"));
            Assert.That(passSource, Does.Contain("BindIndirectLightingParameters"));
            Assert.That(passSource, Does.Contain("PrepareSkyIblState"));
            Assert.That(passSource, Does.Contain("VividPreIntegratedFGDTextures"));
            Assert.That(passSource, Does.Contain("PassRecorder.ImportTexture"));
            Assert.That(passSource, Does.Contain("PreIntegratedFGD_GGXDisneyDiffuse"));
            Assert.That(passSource, Does.Contain("PreIntegratedFGD_CharlieAndFabric"));
            Assert.That(passSource, Does.Contain("MaterialPixelIndicesId"));
            Assert.That(passSource, Does.Contain("MaterialDispatchArgsId"));
            Assert.That(passSource, Does.Contain("DispatchMaterialClass"));
            Assert.That(passSource, Does.Contain("SetComputeTextureParam"));
            Assert.That(passSource, Does.Contain("SetComputeVectorParam"));
            Assert.That(passSource, Does.Contain("SetComputeBufferParam"));
            Assert.That(passSource, Does.Contain("DispatchCompute"));
            Assert.That(passSource, Does.Not.Contain("DrawProceduralIndirect"));
            Assert.That(passSource, Does.Not.Contain("MeshTopology.Points"));
            Assert.That(passSource, Does.Not.Contain("DeferredDirectionalLightingIndirectShaderName"));
            Assert.That(passSource, Does.Not.Contain("SetGlobalTexture("));
            Assert.That(passSource, Does.Not.Contain("SetGlobalColor("));
            Assert.That(passSource, Does.Not.Contain("SetGlobalVector("));
            Assert.That(passSource, Does.Not.Contain("RenderPreIntegratedFGD"));
        }

        private static string GetShaderSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirect.shader");

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");
            return shaderPath;
        }

        private static string GetPassSourcePath()
        {
            var passPath = GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl");

            Assert.That(File.Exists(passPath), Is.True, $"Expected shader pass source at '{passPath}'.");
            return passPath;
        }

        private static string GetRenderPassSourcePath()
        {
            var passPath = GetPackageFilePath("Runtime", "RenderPass", "Core", "DeferredDirectionalLightingPass.cs");

            Assert.That(File.Exists(passPath), Is.True, $"Expected render pass source at '{passPath}'.");
            return passPath;
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
