using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
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
        public void NativeTexturePtrCache_ReusesPointerUntilTextureChanges()
        {
            var firstTexture = new RenderTexture(16, 16, 0);
            var secondTexture = new RenderTexture(16, 16, 0);
            var resolveCount = 0;
            var cache = new DLSSRayReconstructionTexturePtrCache(
                _ => new IntPtr(++resolveCount));
            const DLSSRayReconstructionTexturePtrCache.Slot slot =
                DLSSRayReconstructionTexturePtrCache.Slot.ColorInput;

            try
            {
                var firstPointer = cache.Get(slot, firstTexture);

                Assert.That(
                    cache.Get(slot, firstTexture),
                    Is.EqualTo(firstPointer));
                Assert.That(resolveCount, Is.EqualTo(1));

                var secondPointer = cache.Get(slot, secondTexture);

                Assert.That(secondPointer, Is.Not.EqualTo(firstPointer));
                Assert.That(
                    cache.Get(slot, secondTexture),
                    Is.EqualTo(secondPointer));
                Assert.That(resolveCount, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstTexture);
                UnityEngine.Object.DestroyImmediate(secondTexture);
            }
        }

        [Test]
        public void NativeTexturePtrCache_RefreshesPointerWhenDescriptorChanges()
        {
            var texture = new RenderTexture(16, 16, 0);
            var resolveCount = 0;
            var cache = new DLSSRayReconstructionTexturePtrCache(
                _ => new IntPtr(++resolveCount));
            const DLSSRayReconstructionTexturePtrCache.Slot slot =
                DLSSRayReconstructionTexturePtrCache.Slot.ColorInput;

            try
            {
                var firstPointer = cache.Get(slot, texture);
                texture.width = 32;

                var resizedPointer = cache.Get(slot, texture);

                Assert.That(resizedPointer, Is.Not.EqualTo(firstPointer));
                Assert.That(resolveCount, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
#endif
    }
}
