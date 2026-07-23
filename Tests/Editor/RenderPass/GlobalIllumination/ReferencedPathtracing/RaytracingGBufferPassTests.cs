using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class RaytracingGBufferPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredRaytracingGBufferPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(RaytracingGBufferPass);
        }

        [Test]
        public void Initialize_DefinesNrdAndDlssRayReconstructionGuides()
        {
            IRenderPass renderPass = new RaytracingGBufferPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.AccelerationStructures.Single().Name, Is.EqualTo("SceneRTAS"));
            Assert.That(
                resources.Textures.Select(resource => resource.Name),
                Is.EquivalentTo(new[]
                {
                    "NrdViewZ",
                    "MotionVectors",
                    "DlssMotionVectors",
                    "NrdNormalRoughness",
                    "BaseColorMetalness",
                    "DlssNormalRoughness",
                    "DiffuseAlbedo",
                    "SpecularAlbedo",
                    "NrdDiffuseMaterialFactor",
                    "NrdSpecularMaterialFactor",
                    "DlssDepth"
                }));
            AssertFormat(resources, "NrdViewZ", GraphicsFormat.R32_SFloat);
            AssertFormat(resources, "MotionVectors", GraphicsFormat.R16G16B16A16_SFloat);
            AssertFormat(resources, "DlssMotionVectors", GraphicsFormat.R16G16_SFloat);
            AssertFormat(
                resources,
                "NrdNormalRoughness",
                GraphicsFormat.A2B10G10R10_UNormPack32);
            AssertFormat(resources, "BaseColorMetalness", GraphicsFormat.R8G8B8A8_UNorm);
            AssertFormat(
                resources,
                "DlssNormalRoughness",
                GraphicsFormat.R16G16B16A16_SFloat);
            AssertFormat(resources, "DiffuseAlbedo", GraphicsFormat.A2B10G10R10_UNormPack32);
            AssertFormat(resources, "SpecularAlbedo", GraphicsFormat.A2B10G10R10_UNormPack32);
            AssertFormat(
                resources,
                "NrdDiffuseMaterialFactor",
                GraphicsFormat.R16G16B16A16_SFloat);
            AssertFormat(
                resources,
                "NrdSpecularMaterialFactor",
                GraphicsFormat.R16G16B16A16_SFloat);
            AssertFormat(resources, "DlssDepth", GraphicsFormat.R32_SFloat);
            Assert.That(resources.Textures.All(resource => resource.Access == AccessFlags.Write), Is.True);
        }

        [Test]
        public void Prepare_ResizesAllGuidesAndRejectsOrthographicCamera()
        {
            var gameObject = new GameObject("RaytracingGBufferPassTests.Camera");
            var camera = gameObject.AddComponent<Camera>();
            camera.orthographic = true;

            try
            {
                var pass = new RaytracingGBufferPass();
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.SetCamera(camera);
                cameraData.actualWidth = 640;
                cameraData.actualHeight = 360;

                pass.Prepare(frameData);

                Assert.That(GetField<bool>(pass, "m_ShouldSkipExecution"), Is.True);
                foreach (var fieldName in new[]
                {
                    "m_ViewZ",
                    "m_MotionVectors",
                    "m_DlssMotionVectors",
                    "m_NrdNormalRoughness",
                    "m_BaseColorMetalness",
                    "m_DlssNormalRoughness",
                    "m_DiffuseAlbedo",
                    "m_SpecularAlbedo",
                    "m_NrdDiffuseMaterialFactor",
                    "m_NrdSpecularMaterialFactor",
                    "m_DlssDepth"
                })
                {
                    var texture = GetField<RenderGraphTexture>(pass, fieldName);
                    Assert.That(texture.desc.Width, Is.EqualTo(640), fieldName);
                    Assert.That(texture.desc.Height, Is.EqualTo(360), fieldName);
                    Assert.That(texture.desc.EnableRandomWrite, Is.True, fieldName);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RenderGraphNode_ExposesSceneRtasAndGuideOutputs()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredRaytracingGBufferPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SceneAccelerationStructure"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ViewZ"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_MotionVectors"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DlssMotionVectors"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_NrdNormalRoughness"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DlssNormalRoughness"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DiffuseAlbedo"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_SpecularAlbedo"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_NrdDiffuseMaterialFactor"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_NrdSpecularMaterialFactor"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DlssDepth"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void StandardLitShader_DeclaresRaytracingGBufferDxrPass()
        {
            var shader = Shader.Find("VividRP/Material/StandardLit");
            Assert.That(shader, Is.Not.Null);

            var material = new Material(shader);
            try
            {
                var passIndex = material.FindPass(RaytracingGBufferPass.MaterialShaderPassName);

                Assert.That(passIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    shader.FindPassTagValue(passIndex, new ShaderTagId("LightMode")),
                    Is.EqualTo(new ShaderTagId(RaytracingGBufferPass.MaterialShaderPassName)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void AssertFormat(
            PassResource resources,
            string name,
            GraphicsFormat expected)
        {
            var resource = resources.Textures.Single(texture => texture.Name == name);
            Assert.That(resource.Texture.desc.ColorFormat, Is.EqualTo(expected), name);
            Assert.That(resource.Texture.desc.EnableRandomWrite, Is.True, name);
        }

        private static T GetField<T>(RaytracingGBufferPass pass, string fieldName)
        {
            var field = typeof(RaytracingGBufferPass).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }
    }
}
