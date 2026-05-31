using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
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
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirect.shader"));
            var hlslSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl"));

            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/DeferredDirectionalLightingIndirect\""));
            Assert.That(hlslSource, Does.Contain("StructuredBuffer<uint> _MaterialPixelIndices;"));
            Assert.That(hlslSource, Does.Contain("_LightingWidth"));
            Assert.That(hlslSource, Does.Contain("_LightingHeight"));
            Assert.That(hlslSource, Does.Contain("_DirectionalLightCount"));
            Assert.That(hlslSource, Does.Not.Contain("_PunctualLightCount"));
            Assert.That(hlslSource, Does.Not.Contain("_AreaLightCount"));
            Assert.That(hlslSource, Does.Not.Contain("HasPunctualLights()"));
            Assert.That(hlslSource, Does.Not.Contain("HasAreaLights()"));
            Assert.That(hlslSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/LightingLoop.hlsl\""));
            Assert.That(hlslSource, Does.Contain("VividLightingLoop::Create"));
            Assert.That(hlslSource, Does.Contain("VividLightingLoop::GetPunctualLightCount"));
            Assert.That(hlslSource, Does.Contain("VividLightingLoop::LoadPunctualLight"));
            Assert.That(hlslSource, Does.Contain("VividLightingLoop::GetAreaLightCount"));
            Assert.That(hlslSource, Does.Contain("VividLightingLoop::LoadAreaLight"));
            Assert.That(hlslSource, Does.Not.Contain("for (uint areaLightIndex = 0; areaLightIndex < _AreaLightCount; areaLightIndex++)"));
            Assert.That(hlslSource, Does.Contain("EvaluateDeferredDirectionalLighting"));
            Assert.That(hlslSource, Does.Contain("GetVividPreLightData"));
            Assert.That(hlslSource, Does.Contain("VividAggregateLighting aggregateLighting"));
            Assert.That(hlslSource, Does.Contain("EvaluateBSDF_Env("));
            Assert.That(hlslSource, Does.Contain("EvaluateBSDF_Directional("));
            Assert.That(hlslSource, Does.Contain("EvaluateBSDF_Punctual("));
            Assert.That(hlslSource, Does.Contain("EvaluateBSDF_Area("));
            Assert.That(hlslSource, Does.Contain("PostEvaluateBSDF("));
            Assert.That(hlslSource, Does.Contain("ComputeWorldSpacePosition"));
        }

        [Test]
        public void DeferredLightingPass_UsesUnsafeComputeDispatchAndBindsClusteredLightDataDirectly()
        {
            var passSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "DeferredLightingPass.cs"));

            Assert.That(passSource, Does.Contain("DeferredLitCompute"));
            Assert.That(passSource, Does.Contain("VividPreIntegratedFGD"));
            Assert.That(passSource, Does.Contain("GetNativeCommandBuffer"));
            Assert.That(passSource, Does.Contain("PreparePreIntegratedFGDResources"));
            Assert.That(passSource, Does.Contain("BindIndirectLightingParameters"));
            Assert.That(passSource, Does.Contain("BindSkyTextureParameters"));
            Assert.That(passSource, Does.Contain("BindLightLoopParameters"));
            Assert.That(passSource, Does.Contain("PrepareSkyTextureState"));
            Assert.That(passSource, Does.Contain("Shader.PropertyToID(\"_GTAOTexture\")"));
            Assert.That(passSource, Does.Contain("Shader.PropertyToID(\"_SkyTexture\")"));
            Assert.That(passSource, Does.Contain("Shader.PropertyToID(\"_SkyTextureTint\")"));
            Assert.That(passSource, Does.Contain("Shader.PropertyToID(\"_SkyTextureParams\")"));
            Assert.That(passSource, Does.Contain("Shader.PropertyToID(\"_PixelCoordToViewDirWS\")"));
            Assert.That(passSource, Does.Contain("m_PixelCoordToViewDirWS = cameraData.GetPixelCoordToViewDirWSMatrix();"));
            Assert.That(passSource, Does.Contain("BindSkyTextureParameters(cmd, m_ClearDeferredLitKernel);"));
            Assert.That(passSource, Does.Contain("SetComputeMatrixParam(m_DeferredLitCompute, PixelCoordToViewDirWSId, m_PixelCoordToViewDirWS);"));
            Assert.That(passSource, Does.Contain("VividPreIntegratedFGDTextures"));
            Assert.That(passSource, Does.Contain("PassRecorder.ImportTexture"));
            Assert.That(passSource, Does.Contain("DirectionalLightsId"));
            Assert.That(passSource, Does.Contain("PunctualLightsId"));
            Assert.That(passSource, Does.Contain("AreaLightsId"));
            Assert.That(passSource, Does.Contain("ReflectionProbesId"));
            Assert.That(passSource, Does.Contain("LayeredOffsetId"));
            Assert.That(passSource, Does.Contain("LayeredLightListId"));
            Assert.That(passSource, Does.Contain("LogBaseBufferId"));
            Assert.That(passSource, Does.Contain("SetComputeIntParam(m_DeferredLitCompute, DirectionalLightCountId"));
            Assert.That(passSource, Does.Contain("ClusteredPunctualLightGridEnabledId"));
            Assert.That(passSource, Does.Contain("ClusteredAreaLightGridEnabledId"));
            Assert.That(passSource, Does.Contain("ClusteredReflectionProbeGridEnabledId"));
            Assert.That(passSource, Does.Contain("SetComputeFloatParam(m_DeferredLitCompute, ClusterScaleId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, DirectionalLightsId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, PunctualLightsId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, AreaLightsId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, ReflectionProbesId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, LayeredOffsetId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, LayeredLightListId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, LogBaseBufferId"));
            Assert.That(passSource, Does.Contain("PrepareClusteredLightingParameters"));
            Assert.That(passSource, Does.Contain("m_LocalGTAOTexture"));
            Assert.That(passSource, Does.Not.Contain("PunctualLightCountId"));
            Assert.That(passSource, Does.Not.Contain("AreaLightCountId"));
            Assert.That(passSource, Does.Not.Contain("LightGridGlobalPass"));
            Assert.That(passSource, Does.Not.Contain("SetGlobalInt("));
            Assert.That(passSource, Does.Not.Contain("SetGlobalFloat("));
            Assert.That(passSource, Does.Not.Contain("SetGlobalBuffer("));
            Assert.That(passSource, Does.Not.Contain("DrawProceduralIndirect"));
            Assert.That(passSource, Does.Not.Contain("MeshTopology.Points"));
        }

        [Test]
        public void DeferredDirectionalLightingPass_RemainsCompatibilityWrapper()
        {
            var wrapperSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "DeferredDirectionalLightingPass.cs"));

            Assert.That(wrapperSource, Does.Contain("class DeferredDirectionalLightingPass : DeferredLightingPass"));
            Assert.That(wrapperSource, Does.Contain("base(nameof(DeferredDirectionalLightingPass))"));
            Assert.That(wrapperSource, Does.Contain("DeferredLightingPass.BuildSkyTextureParams"));
            Assert.That(wrapperSource, Does.Contain("DeferredLightingPass.BuildSkyIblParams"));
        }

        [Test]
        public void GeneratedNodeRegistry_ContainsDeferredLightingPassNode()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "RenderGraph", "GeneratedRenderPassNodes.g.cs"));

            Assert.That(source, Does.Contain("internal sealed class DeferredLightingPass : RenderPassNodeData"));
            Assert.That(source, Does.Contain("VividRP.Runtime.RenderPass.Core.DeferredLightingPass, VividRP.Runtime"));
            Assert.That(source, Does.Contain("internal sealed class DeferredDirectionalLightingPass : RenderPassNodeData"));
        }

        [Test]
        public void DefaultRenderGraph_UsesDeferredLightingPassAndDirectLightGridConnections()
        {
            var graphSource = File.ReadAllText(GetDefaultRenderGraphAssetPath());

            Assert.That(graphSource, Does.Contain("type: {class: DeferredLightingPass, ns: VividRP.Editor.RenderGraph.Generated, asm: VividRP.Editor}"));
            AssertWireExists(graphSource, "m_DirectionalLightBuffer", "DirectionalLights");
            AssertWireExists(graphSource, "m_PunctualLightBuffer", "PunctualLights");
            AssertWireExists(graphSource, "m_DecalDataBuffer", "DecalData");
            AssertWireExists(graphSource, "m_LayeredOffsetBuffer", "LayeredOffset");
            AssertWireExists(graphSource, "m_LayeredLightListBuffer", "LayeredLightList");
            AssertWireExists(graphSource, "m_LogBaseBuffer", "LogBaseBuffer");
        }

        private static void AssertWireExists(string graphSource, string uniqueId, string title)
        {
            var pattern =
                $@"m_FromPortReference:\s*[\s\S]*?m_UniqueId: {Regex.Escape(uniqueId)}\s*[\s\S]*?m_Title: {Regex.Escape(title)} \(W\)\s*[\s\S]*?m_ToPortReference:\s*[\s\S]*?m_UniqueId: {Regex.Escape(uniqueId)}\s*[\s\S]*?m_Title: {Regex.Escape(title)} \(R\)";

            Assert.That(graphSource, Does.Match(pattern), $"Expected a LightGrid consumer wire for '{uniqueId}'.");
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

        private static string GetDefaultRenderGraphAssetPath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var candidatePaths = new[]
            {
                Path.Combine(projectRoot, "Assets", "Vivid Render Graph.vrdg"),
                Path.Combine(projectRoot, "Assets", "Hybrid.vrdg"),
            };

            foreach (var candidatePath in candidatePaths)
            {
                if (File.Exists(candidatePath))
                    return candidatePath;
            }

            return candidatePaths[0];
        }
    }
}
