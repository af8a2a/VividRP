using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapReceiverQualityTests
    {
        [Test]
        public void SMRTHistory_StableDescriptorAndCameraLookupAllocateNoManagedMemory()
        {
            var cameraObject = new GameObject("VSM history allocation test");
            var camera = cameraObject.AddComponent<Camera>();
            using var states = new CameraRelativeSystem<CSMShadowResolvePass.ShadowHistoryState>();
            var descriptor = RenderGraphTextureDesc.CreateColorTarget(33, 25,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
            try
            {
                for (int i = 0; i < 64; i++)
                {
                    states.GetOrCreateBase(camera);
                    CSMShadowResolvePass.ConfigureHistoryDescriptor(descriptor, 33, 25);
                    CameraHistoryRenderGraphBridge.CreateDescriptor(descriptor);
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                {
                    states.PurgeDestroyedCameras();
                    states.GetOrCreateBase(camera);
                    CSMShadowResolvePass.ConfigureHistoryDescriptor(descriptor, 33, 25);
                    CameraHistoryRenderGraphBridge.CreateDescriptor(descriptor);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void Volume_DefaultsToLegacyAndClampsIndependentQualityInputs()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            try
            {
                Assert.That(settings.virtualShadowMapScreenDensity.value, Is.False);
                Assert.That(settings.virtualShadowMapTargetTexelPixels.value, Is.EqualTo(1));
                Assert.That(settings.virtualShadowMapResolutionLodBias.value, Is.Zero);
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(settings), Is.EqualTo(new Vector4(0, 1, 0.1f, 1)));
                settings.virtualShadowMapTargetTexelPixels.value = 0;
                settings.virtualShadowMapResolutionLodBias.value = -100;
                Assert.That(settings.virtualShadowMapTargetTexelPixels.value, Is.EqualTo(0.25f));
                Assert.That(settings.virtualShadowMapResolutionLodBias.value, Is.EqualTo(-4));
                settings.virtualShadowMapTargetTexelPixels.value = 100;
                settings.virtualShadowMapResolutionLodBias.value = 100;
                Assert.That(settings.virtualShadowMapTargetTexelPixels.value, Is.EqualTo(8));
                Assert.That(settings.virtualShadowMapResolutionLodBias.value, Is.EqualTo(4));
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(true, 1, -1).y, Is.EqualTo(0.5f));
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(true, 1, 1).y, Is.EqualTo(2));
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        [Test]
        public void QualityUniformChanges_KeepProjectionBuffersPageOriginsAndGeneration()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            using var projections = new VirtualShadowMapProjectionSet();
            using var cmd = new CommandBuffer();
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100);
            layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 2048, 1, 1, 1, 2);
            projections.PrepareClipmaps(layout);
            projections.CommitRecordedLayout();
            var buffer = projections.Buffer;
            var generation = projections.Generation;
            var matrix = layout.Projections[0] * layout.Views[0];
            var origin = layout.OriginX[0];
            for (int i = -4; i <= 4; i++)
            {
                // The only quality input to rendering is a receiver uniform;
                // camera FOV/output-size matrices never enter the layout producer.
                var quality = VirtualShadowMapReceiverQuality.BuildParameters(true, 1, i);
                cmd.SetGlobalVector(VirtualShadowMapReceiverQuality.ParametersId, quality);
                layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 2048, 1, 1, 1, 2);
                projections.PrepareClipmaps(layout);
                Assert.That(projections.Buffer, Is.SameAs(buffer));
                Assert.That(projections.Generation, Is.EqualTo(generation));
                Assert.That(projections.RequiresRemap || projections.RequiresFeedbackReset, Is.False);
                Assert.That(layout.Projections[0] * layout.Views[0], Is.EqualTo(matrix));
                Assert.That(layout.OriginX[0], Is.EqualTo(origin));
            }
        }

        [Test]
        public void StableQualityParameterPreparation_AllocatesZeroBytes()
        {
            using var cmd = new CommandBuffer();
            for (int i = 0; i < 32; i++) RecordQuality(cmd);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) RecordQuality(cmd);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void SMRT_ZeroAnglePreservesReferenceAndStableBindingDoesNotAllocate()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            using var cmd = new CommandBuffer();
            try
            {
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTParameters(settings, .5f), Is.EqualTo(Vector4.zero));
                settings.virtualShadowMapSMRT.value = true;
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTParameters(settings, 0), Is.EqualTo(Vector4.zero));
                Vector4 parameters = VirtualShadowMapReceiverQuality.BuildSMRTParameters(settings, .5f);
                Assert.That(parameters.x, Is.EqualTo(4)); Assert.That(parameters.y, Is.EqualTo(8));
                Assert.That(parameters.w, Is.EqualTo(Mathf.Tan(.25f * Mathf.Deg2Rad)).Within(1e-7));
                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
                Assert.That(shader, Is.Not.Null);
                for (int i = 0; i < 32; i++) RecordSMRT(cmd, settings, shader);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++) RecordSMRT(cmd, settings, shader);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bytes, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        [Test]
        public void SMRT_StableExplicitBlueNoiseBindingDoesNotAllocate()
        {
            bool ownsBlueNoise = BlueNoise.Instance == null;
            BlueNoise.Initialize();
            using var cmd = new CommandBuffer();
            try
            {
                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
                int kernel = shader.FindKernel("CSMShadowResolve");
                var noise = BlueNoise.Instance;
                for (int i = 0; i < 32; i++) { cmd.Clear(); noise.Bind(cmd, shader, kernel); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++) { cmd.Clear(); noise.Bind(cmd, shader, kernel); }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bytes, Is.Zero);
            }
            finally { if (ownsBlueNoise) BlueNoise.Cleanup(); }
        }

        [TestCase(8)]
        [TestCase(18)]
        [TestCase(24)]
        [TestCase(32)]
        [TestCase(72)]
        [TestCase(17)]
        [TestCase(31)]
        public void SMRTJoint_VisitsEveryBNDIndexAtEachJitterPhase(int phases)
        {
            for (int phase = 0; phase < phases; phase++)
            {
                var counts = new int[256];
                for (int cycle = 0; cycle < 256; cycle++)
                {
                    int frame = cycle * phases + phase;
                    int offset = VirtualShadowMapReceiverQuality.CalculateSMRTSampleIndexOffset(frame, phases);
                    counts[(frame + offset) & 255]++;
                    if ((phases & 1) != 0) Assert.That(offset, Is.Zero);
                }
                Assert.That(counts, Is.All.EqualTo(1), "Every index must occur once at a fixed jitter phase.");
            }
        }

        [Test]
        public void SMRTJoint_RequiresEnabledSMRTAndActiveTSRJitter()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            var go = new GameObject("SMRT joint sampling test", typeof(Camera), typeof(VividAdditionalCameraData));
            var camera = go.GetComponent<VividAdditionalCameraData>();
            try
            {
                camera.enableTSR = true;
                camera.SetTsrJitterData(Vector2.zero, 8);
                settings.virtualShadowMapSMRT.value = true;
                Assert.That(settings.virtualShadowMapSMRTJointSampling.value, Is.False);
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTSampleIndexOffset(settings, camera, 8), Is.Zero);
                settings.virtualShadowMapSMRTJointSampling.value = true;
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTSampleIndexOffset(settings, camera, 8), Is.EqualTo(1));
                camera.ResetTsrJitterData();
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTSampleIndexOffset(settings, camera, 8), Is.Zero);
                camera.SetTsrJitterData(Vector2.zero, 8);
                camera.antialiasing = VividAntialiasingMode.TemporalAntiAliasing;
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTSampleIndexOffset(settings, camera, 8), Is.Zero);
                camera.enableTSR = true;
                settings.virtualShadowMapSMRT.value = false;
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTSampleIndexOffset(settings, camera, 8), Is.Zero);
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTSampleIndexOffset(null, camera, 8), Is.Zero);
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTSampleIndexOffset(settings, null, 8), Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(settings); }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public void SMRTJoint_InactivePhaseCountsKeepTheOriginalIndex(int phases)
        {
            Assert.That(VirtualShadowMapReceiverQuality.CalculateSMRTSampleIndexOffset(int.MaxValue, phases), Is.Zero);
            Assert.That(VirtualShadowMapReceiverQuality.CalculateSMRTSampleIndexOffset(-1, 8), Is.Zero);
        }

        [Test]
        public void SMRTJoint_IndexRepeatsAfterACompleteJointPeriodNearFrameLimit()
        {
            const int period = 8 * 256;
            for (int frame = int.MaxValue - 8; frame < int.MaxValue; frame++)
            {
                uint index = ((uint)frame + (uint)VirtualShadowMapReceiverQuality.CalculateSMRTSampleIndexOffset(frame, 8)) & 255u;
                int earlier = frame - period;
                uint repeated = ((uint)earlier + (uint)VirtualShadowMapReceiverQuality.CalculateSMRTSampleIndexOffset(earlier, 8)) & 255u;
                Assert.That(index, Is.EqualTo(repeated));
            }
        }

        [Test]
        public void CoverageAndLodTransitions_AreIndependentAndCanBeZero()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            try
            {
                settings.virtualShadowMapTransition.value = .4f;
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(settings).z, Is.EqualTo(.2f));
                settings.virtualShadowMapCoverageTransition.Override(.05f);
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(settings).z, Is.EqualTo(.025f));
                settings.virtualShadowMapCoverageTransition.value = 0;
                var parameters = VirtualShadowMapReceiverQuality.BuildParameters(settings);
                Assert.That(parameters.z, Is.Zero);
                Assert.That(parameters.w, Is.EqualTo(1));
                Assert.That(settings.virtualShadowMapTransition.value, Is.EqualTo(.4f));
                Assert.That(settings.virtualShadowMapViewCoverage.value, Is.False);
                Assert.That(settings.virtualShadowMapPhysicalPageBudget.value, Is.EqualTo(256));
                settings.virtualShadowMapPhysicalPageBudget.value = 10000;
                Assert.That(settings.virtualShadowMapPhysicalPageBudget.value, Is.EqualTo(1024));
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        private static void RecordSMRT(CommandBuffer cmd, CascadedShadowSettingsVolume settings, ComputeShader shader)
        {
            cmd.Clear();
            cmd.SetComputeVectorParam(shader, VirtualShadowMapReceiverQuality.SMRTParametersId,
                VirtualShadowMapReceiverQuality.BuildSMRTParameters(settings, .5f));
        }

        private static void RecordQuality(CommandBuffer cmd)
        {
            cmd.Clear();
            cmd.SetGlobalVector(VirtualShadowMapReceiverQuality.ParametersId,
                VirtualShadowMapReceiverQuality.BuildParameters(true, 1, -1));
            cmd.SetGlobalMatrix(VirtualShadowMapReceiverQuality.ViewProjectionId, Matrix4x4.identity);
        }
    }
}
