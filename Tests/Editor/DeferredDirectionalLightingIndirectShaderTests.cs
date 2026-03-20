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
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirect.shader"));
            var hlslSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl"));

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
        public void DeferredLightingPass_UsesUnsafeComputeDispatchAndBindsClusteredLightDataDirectly()
        {
            var passSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "DeferredLightingPass.cs"));

            Assert.That(passSource, Does.Contain("DeferredLitCompute"));
            Assert.That(passSource, Does.Contain("VividPreIntegratedFGD"));
            Assert.That(passSource, Does.Contain("GetNativeCommandBuffer"));
            Assert.That(passSource, Does.Contain("PreparePreIntegratedFGDResources"));
            Assert.That(passSource, Does.Contain("BindIndirectLightingParameters"));
            Assert.That(passSource, Does.Contain("BindLightLoopParameters"));
            Assert.That(passSource, Does.Contain("PrepareSkyIblState"));
            Assert.That(passSource, Does.Contain("VividPreIntegratedFGDTextures"));
            Assert.That(passSource, Does.Contain("PassRecorder.ImportTexture"));
            Assert.That(passSource, Does.Contain("DirectionalLightsId"));
            Assert.That(passSource, Does.Contain("PunctualLightsId"));
            Assert.That(passSource, Does.Contain("LayeredOffsetId"));
            Assert.That(passSource, Does.Contain("LayeredLightListId"));
            Assert.That(passSource, Does.Contain("LogBaseBufferId"));
            Assert.That(passSource, Does.Contain("SetComputeIntParam(m_DeferredLitCompute, DirectionalLightCountId"));
            Assert.That(passSource, Does.Contain("SetComputeFloatParam(m_DeferredLitCompute, ClusterScaleId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, DirectionalLightsId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, PunctualLightsId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, LayeredOffsetId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, LayeredLightListId"));
            Assert.That(passSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, LogBaseBufferId"));
            Assert.That(passSource, Does.Contain("PrepareClusteredLightingParameters"));
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
            var graphSource = File.ReadAllText(GetAssetFilePath("Assets", "Vivid Render Graph.vrdg"));

            Assert.That(graphSource, Does.Contain("type: {class: DeferredLightingPass, ns: VividRP.Editor.RenderGraph.Generated, asm: VividRP.Editor}"));
            Assert.That(graphSource, Does.Contain(
@"m_ToPortReference:
          m_NodeModelGuid:
            m_Value0: 2551610526311484606
            m_Value1: 4936634331650870990
          m_NodeModelHashGuid:
            serializedVersion: 2
            Hash: be8cf13141246923ce4e0cecc4728244
          m_UniqueId: m_DirectionalLightBuffer"));
            Assert.That(graphSource, Does.Contain(
@"m_ToPortReference:
          m_NodeModelGuid:
            m_Value0: 2551610526311484606
            m_Value1: 4936634331650870990
          m_NodeModelHashGuid:
            serializedVersion: 2
            Hash: be8cf13141246923ce4e0cecc4728244
          m_UniqueId: m_PunctualLightBuffer"));
            Assert.That(graphSource, Does.Contain(
@"m_ToPortReference:
          m_NodeModelGuid:
            m_Value0: 2551610526311484606
            m_Value1: 4936634331650870990
          m_NodeModelHashGuid:
            serializedVersion: 2
            Hash: be8cf13141246923ce4e0cecc4728244
          m_UniqueId: m_LayeredOffsetBuffer"));
            Assert.That(graphSource, Does.Contain(
@"m_ToPortReference:
          m_NodeModelGuid:
            m_Value0: 2551610526311484606
            m_Value1: 4936634331650870990
          m_NodeModelHashGuid:
            serializedVersion: 2
            Hash: be8cf13141246923ce4e0cecc4728244
          m_UniqueId: m_LayeredLightListBuffer"));
            Assert.That(graphSource, Does.Contain(
@"m_ToPortReference:
          m_NodeModelGuid:
            m_Value0: 2551610526311484606
            m_Value1: 4936634331650870990
          m_NodeModelHashGuid:
            serializedVersion: 2
            Hash: be8cf13141246923ce4e0cecc4728244
          m_UniqueId: m_LogBaseBuffer"));
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

        private static string GetAssetFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, Path.Combine(relativeParts));
        }
    }
}
