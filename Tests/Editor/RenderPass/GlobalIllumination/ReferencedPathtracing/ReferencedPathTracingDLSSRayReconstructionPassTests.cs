using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingDLSSRayReconstructionPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredDLSSRRPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() =>
                typeof(ReferencedPathTracingDLSSRayReconstructionPass);

#if DLSS_PLUGIN_INTEGRATE
            internal bool TryGetPreset(out DLSSRRPreset value)
            {
                return TryGetEnumParameterValue("m_Preset", out value);
            }
#endif
        }

        [Test]
        public void Pass_PreservesNativeTemporalSideEffects()
        {
            Assert.That(
                typeof(IRenderGraphSideEffectPass).IsAssignableFrom(
                    typeof(ReferencedPathTracingDLSSRayReconstructionPass)),
                Is.True);
        }

        [Test]
        public void Initialize_DefinesDLSSRRGuidesAndNativeResolutionOutput()
        {
            IRenderPass renderPass =
                new ReferencedPathTracingDLSSRayReconstructionPass();

            var resources = renderPass.Initialize();

            Assert.That(
                resources.Textures.Select(resource => resource.Name),
                Is.EquivalentTo(new[]
                {
                    "PathTracingRadiance",
                    "DlssDepth",
                    "DlssMotionVectors",
                    "DlssNormalRoughness",
                    "DiffuseAlbedo",
                    "SpecularAlbedo",
                    "PathTracingEmission",
                    "DiffuseRayDirectionHitDistance",
                    "SpecularRayDirectionHitDistance",
                    "DLSSRRResolvedColor",
                    "DLSSRRSceneLinearColor"
                }));
            var sceneLinearScratch = resources.Textures.Single(resource =>
                resource.Name == "DLSSRRSceneLinearColor");
            Assert.That(sceneLinearScratch.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(sceneLinearScratch.IsTransient, Is.True);
            Assert.That(
                sceneLinearScratch.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(sceneLinearScratch.Texture.desc.EnableRandomWrite, Is.True);
            Assert.That(
                resources.Textures.Single(resource =>
                    resource.Name == "DLSSRRResolvedColor").Access,
                Is.EqualTo(AccessFlags.WriteAll));
            Assert.That(
                resources.Textures.Single(resource =>
                    resource.Name == "DLSSRRResolvedColor")
                    .Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(
                resources.Textures.Single(resource =>
                    resource.Name == "DLSSRRResolvedColor")
                    .Texture.desc.EnableRandomWrite,
                Is.True);
            Assert.That(resources.BypassRules, Has.Length.EqualTo(1));
            Assert.That(resources.BypassRules[0].SourceFieldName, Is.EqualTo("m_Radiance"));
            Assert.That(resources.BypassRules[0].OutputFieldName, Is.EqualTo("m_ResolvedColor"));
        }

        [Test]
        public void RenderGraphNode_DoesNotExposeExposureAsGraphResource()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredDLSSRRPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_Radiance"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_Depth"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_MotionVectors"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_NormalRoughness"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DiffuseAlbedo"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_SpecularAlbedo"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_Emissive"), Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_DiffuseRayDirectionHitDistance"),
                    Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_SpecularRayDirectionHitDistance"),
                    Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ExposureTexture"), Is.Null);
                Assert.That(node.GetInputPortByName("m_SceneLinearColor"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_SceneLinearColor"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_ResolvedColor"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

#if DLSS_PLUGIN_INTEGRATE
        [Test]
        public void ApplyEnumParameters_UpdatesRayReconstructionPreset()
        {
            var pass = new ReferencedPathTracingDLSSRayReconstructionPass();

            RenderGraphPassEnumParameterUtility.ApplyEnumParameters(
                pass,
                typeof(ReferencedPathTracingDLSSRayReconstructionPass),
                new List<RenderGraphPassEnumParameter>
                {
                    new()
                    {
                        FieldName = "m_Preset",
                        Value = (int)DLSSRRPreset.E
                    }
                });

            Assert.That(pass.Preset, Is.EqualTo(DLSSRRPreset.E));
        }

        [Test]
        public void RenderGraphNode_ExposesRayReconstructionPreset()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredDLSSRRPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.TryGetPreset(out var preset), Is.True);
                Assert.That(preset, Is.EqualTo(DLSSRRPreset.Default));

                var result = RenderGraphCompiler.Compile(graph);
                var parameter = result.Passes.Single()
                    .EnumParameters.Single(entry => entry.FieldName == "m_Preset");
                Assert.That(parameter.Value, Is.EqualTo((int)DLSSRRPreset.Default));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
#endif

        [Test]
        public void ResolveShader_RestoresVividPreExposureAfterSceneLinearRR()
        {
            const string assetPath =
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathTracingDLSSRayReconstructionResolve.compute";
            var shader = AssetDatabase.LoadAssetAtPath<UnityEngine.ComputeShader>(
                assetPath);

            Assert.That(shader, Is.Not.Null);
            Assert.That(
                shader.FindKernel(
                    ReferencedPathTracingDLSSRayReconstructionPass
                        .ResolvePreExposureKernelName),
                Is.GreaterThanOrEqualTo(0));

            var source = File.ReadAllText(Path.GetFullPath(assetPath));
            Assert.That(source, Does.Contain("VividApplyPreExposure"));
            Assert.That(
                source,
                Does.Contain("_ReferencedPathTracingDLSSRRSceneLinearColor"));
        }
    }
}
