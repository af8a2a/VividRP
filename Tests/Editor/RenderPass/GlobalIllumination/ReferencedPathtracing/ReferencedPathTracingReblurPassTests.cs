using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
    public sealed class ReferencedPathTracingReblurPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredReblurPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ReferencedPathTracingReblurPass);
        }

        [Test]
        public void Pass_PreservesReblurHistoriesWithoutSameFrameConsumer()
        {
            Assert.That(
                typeof(IRenderGraphSideEffectPass).IsAssignableFrom(
                    typeof(ReferencedPathTracingReblurPass)),
                Is.True);
        }

        [Test]
        public void Initialize_DefinesDiffuseSpecularSignalsGuidesAndResolvedOutput()
        {
            IRenderPass renderPass = new ReferencedPathTracingReblurPass();

            var resources = renderPass.Initialize();

            AssertRead(resources, "DiffuseRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "SpecularRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "PathTracingDirectLighting", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "PathTracingEmission", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "NrdDiffuseMaterialFactor", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "NrdSpecularMaterialFactor", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "NrdViewZ", GraphicsFormat.R32_SFloat);
            AssertRead(resources, "MotionVectors", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(
                resources,
                "NrdNormalRoughness",
                GraphicsFormat.A2B10G10R10_UNormPack32);

            AssertWrite(resources, "DenoisedDiffuseRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertWrite(resources, "DenoisedSpecularRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertWrite(resources, "ReblurResolvedColor", GraphicsFormat.R32G32B32A32_SFloat);
        }

        [Test]
        public void SharedConstants_MatchOfficialReblurConstantBufferLayout()
        {
            Assert.That(Marshal.SizeOf<ReblurSharedConstants>(), Is.EqualTo(848));
        }

        [Test]
        public void Volume_DefaultsMatchNrdReblurDefaults()
        {
            var volume = ScriptableObject.CreateInstance<ReferencedPathTracingReblurVolume>();

            try
            {
                Assert.That(volume.enabled.value, Is.True);
                Assert.That(volume.maxAccumulatedFrameNum.value, Is.EqualTo(30));
                Assert.That(volume.maxFastAccumulatedFrameNum.value, Is.EqualTo(6));
                Assert.That(volume.historyFixFrameNum.value, Is.EqualTo(3));
                Assert.That(volume.diffusePrepassBlurRadius.value, Is.EqualTo(30.0f));
                Assert.That(volume.specularPrepassBlurRadius.value, Is.EqualTo(50.0f));
                Assert.That(volume.minBlurRadius.value, Is.EqualTo(1.0f));
                Assert.That(volume.maxBlurRadius.value, Is.EqualTo(30.0f));
                Assert.That(volume.lobeAngleFraction.value, Is.EqualTo(0.15f));
                Assert.That(volume.roughnessFraction.value, Is.EqualTo(0.15f));
                Assert.That(volume.hitDistanceA.value, Is.EqualTo(3.0f));
                Assert.That(volume.hitDistanceB.value, Is.EqualTo(0.1f));
                Assert.That(volume.hitDistanceC.value, Is.EqualTo(20.0f));
                Assert.That(volume.hitDistanceD.value, Is.EqualTo(-25.0f));
                Assert.That(volume.IsActive(), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void SettingsResolver_UsesVolumeOverridesAndMaintainsNrdConstraints()
        {
            var cameraObject = new GameObject("ReferencedPathTracingReblurVolumeTests.Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var volume = profile.Add<ReferencedPathTracingReblurVolume>(true);
            volume.maxAccumulatedFrameNum.value = 12;
            volume.maxFastAccumulatedFrameNum.value = 1;
            volume.historyFixFrameNum.value = 3;
            volume.responsiveAccumulationMinFrameNum.value = 3;
            volume.minBlurRadius.value = 5.0f;
            volume.maxBlurRadius.value = 2.0f;
            volume.hitDistanceA.value = 4.0f;
            volume.hitDistanceB.value = 0.2f;
            volume.hitDistanceC.value = 10.0f;
            volume.hitDistanceD.value = -12.0f;
            volume.enableAntiFirefly.value = true;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var settings = ReferencedPathTracingReblurSettingsResolver.Resolve();

                Assert.That(settings.maxAccumulatedFrameNum, Is.EqualTo(12));
                Assert.That(settings.maxFastAccumulatedFrameNum, Is.EqualTo(1));
                Assert.That(settings.historyFixFrameNum, Is.Zero);
                Assert.That(settings.responsiveAccumulationMinFrameNum, Is.Zero);
                Assert.That(settings.minBlurRadius, Is.EqualTo(2.0f));
                Assert.That(settings.maxBlurRadius, Is.EqualTo(2.0f));
                Assert.That(
                    settings.hitDistanceParameters,
                    Is.EqualTo(new Vector4(4.0f, 0.2f, 10.0f, -12.0f)));
                Assert.That(settings.enableAntiFirefly, Is.True);

                var pass = new ReferencedPathTracingPass();
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.SetCamera(camera);
                pass.Prepare(frameData);
                var hitDistanceField = typeof(ReferencedPathTracingPass).GetField(
                    "m_ReblurHitDistanceParameters",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(hitDistanceField, Is.Not.Null);
                Assert.That(
                    (Vector4)hitDistanceField.GetValue(pass),
                    Is.EqualTo(settings.hitDistanceParameters));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void SharedConstants_UseResolvedReblurSettingsAndResetHistory()
        {
            var settings = ReferencedPathTracingReblurSettings.CreateDefault();
            settings.hitDistanceParameters = new Vector4(4.0f, 0.2f, 10.0f, -12.0f);
            settings.antilagParameters = new Vector2(2.0f, 1.5f);
            settings.maxAccumulatedFrameNum = 12;
            settings.maxFastAccumulatedFrameNum = 4;
            settings.maxBlurRadius = 5.0f;
            settings.fastHistoryClampingSigmaScale = 1.5f;
            settings.lobeAngleFraction = 0.5f;
            settings.enableAntiFirefly = true;
            settings.usePrepassOnlyForSpecularMotionEstimation = true;
            settings.responsiveAccumulationRoughnessThreshold = 0.25f;
            settings.responsiveAccumulationMinFrameNum = 2;
            settings.returnHistoryLengthInsteadOfOcclusion = true;

            var constants = ReblurSharedConstants.Compute(
                null,
                null,
                320,
                180,
                true,
                settings);

            Assert.That(constants.gHitDistParams, Is.EqualTo(settings.hitDistanceParameters));
            Assert.That(constants.gAntilagParams, Is.EqualTo(settings.antilagParameters));
            Assert.That(constants.gMaxAccumulatedFrameNum, Is.EqualTo(12.0f));
            Assert.That(constants.gMaxFastAccumulatedFrameNum, Is.EqualTo(4.0f));
            Assert.That(constants.gFastHistoryClampingSigmaScale, Is.EqualTo(1.5f));
            Assert.That(constants.gLobeAngleFraction, Is.EqualTo(0.25f));
            Assert.That(constants.gAntiFirefly, Is.EqualTo(1.0f));
            Assert.That(constants.gUsePrepassNotOnlyForSpecularMotionEstimation, Is.Zero);
            Assert.That(constants.gResponsiveAccumulationInvRoughnessThreshold, Is.EqualTo(4.0f));
            Assert.That(constants.gResponsiveAccumulationMinAccumulatedFrameNum, Is.EqualTo(2u));
            Assert.That(constants.gReturnHistoryLengthInsteadOfOcclusion, Is.EqualTo(1u));

            constants = ReblurSharedConstants.Compute(
                null,
                null,
                320,
                180,
                false,
                settings);
            Assert.That(constants.gMaxAccumulatedFrameNum, Is.Zero);
            Assert.That(constants.gMaxFastAccumulatedFrameNum, Is.Zero);
            Assert.That(constants.gResetHistory, Is.EqualTo(1u));
        }

        [Test]
        public void RenderGraphNode_ExposesExternalSignalsAndKeepsTransientsInternal()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReblurPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_DiffuseInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_SpecularInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DirectLightingInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_EmissionInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DiffuseMaterialFactorInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_SpecularMaterialFactorInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ViewZInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_MotionVectorsInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_NormalRoughnessInput"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DiffuseOutput"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_SpecularOutput"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ResolvedColor"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_Tiles"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_Tiles"), Is.Null);
                Assert.That(node.GetInputPortByName("m_PreparedDiffuse"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_PreparedDiffuse"), Is.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private static void AssertRead(
            PassResource resources,
            string name,
            GraphicsFormat format)
        {
            var resource = resources.Textures.Single(texture => texture.Name == name);
            Assert.That(resource.Access, Is.EqualTo(AccessFlags.Read), name);
            Assert.That(resource.Texture.desc.ColorFormat, Is.EqualTo(format), name);
        }

        private static void AssertWrite(
            PassResource resources,
            string name,
            GraphicsFormat format)
        {
            var resource = resources.Textures.Single(texture => texture.Name == name);
            Assert.That(resource.Access, Is.EqualTo(AccessFlags.Write), name);
            Assert.That(resource.Texture.desc.ColorFormat, Is.EqualTo(format), name);
            Assert.That(resource.Texture.desc.EnableRandomWrite, Is.True, name);
        }
    }
}
