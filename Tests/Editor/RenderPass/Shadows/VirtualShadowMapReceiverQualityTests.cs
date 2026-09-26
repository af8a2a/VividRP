using VividRP.Runtime.VirtualShadowMap;
using System;
using NUnit.Framework;
using Unity.Mathematics;
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
        public void PagePressure_RequiresDensityAndCanBeDisabledIndependently()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            try
            {
                Assert.That(settings.virtualShadowMapPagePressure.value, Is.True);
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(settings).x, Is.Zero);
                settings.virtualShadowMapScreenDensity.value = true;
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(settings).x, Is.EqualTo(2));
                settings.virtualShadowMapPagePressure.value = false;
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(settings).x, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        [Test]
        public void PagePressure_RespondsToDemandAndWaitsBeforeRecovering()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"));
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
            using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 16);
            var state = new[] { new uint4(math.asuint(1f), 0, 800, 0), new uint4(0, 0, 0, math.asuint(1f)), uint4.zero };
            try
            {
                using var requestFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
                requestFlags.SetData(new uint[1]);
                int clear = shader.FindKernel("VSMPrototypeClearReceiverRequests");
                shader.SetBuffer(clear, "_VSMPageRequestFlags", requestFlags);
                int reset = shader.FindKernel("VSMPrototypeResetReceiverFeedback");
                shader.SetBuffer(reset, "_VSMPageRequestFlags", requestFlags);
                metadata.SetData(new uint4[1]);
                pressure.SetData(state);
                shader.SetInt("_VSMPrototypePageTableEntryCount", 1);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 1024);
                shader.SetInt("_VSMProjectionCount", 4);
                shader.SetVector("_VSMReceiverQuality", new Vector4(2, 1, .1f, 1));
                shader.SetBuffer(clear, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(clear, "_VSMPagePressureRW", pressure);
                shader.SetBuffer(reset, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(reset, "_VSMPagePressureRW", pressure);
                for (int frame = 0; frame < 59; frame++) shader.Dispatch(clear, 1, 1, 1);
                pressure.GetData(state);
                Assert.That(math.asfloat(state[0].x), Is.EqualTo(1f));
                Assert.That(state[0].y, Is.EqualTo(59u));
                shader.Dispatch(clear, 1, 1, 1);
                pressure.GetData(state);
                Assert.That(math.asfloat(state[0].x), Is.EqualTo(1f - 1f / 64));
                Assert.That(state[1].z, Is.EqualTo(math.asuint(1f)));

                // No receivers must not continuously improve quality based on silence.
                state[0] = new uint4(math.asuint(1f), 59, 0, 0);
                pressure.SetData(state);
                shader.Dispatch(clear, 1, 1, 1);
                pressure.GetData(state);
                Assert.That(math.asfloat(state[0].x), Is.EqualTo(1f));
                Assert.That(state[0].y, Is.Zero);

                state[0] = new uint4(math.asuint(1f), 60, 2048, 1024);
                pressure.SetData(state);
                shader.Dispatch(clear, 1, 1, 1);
                pressure.GetData(state);
                Assert.That(math.asfloat(state[0].x), Is.EqualTo(1.125f));
                Assert.That(state[0].y, Is.Zero);
                for (int frame = 0; frame < 64; frame++) shader.Dispatch(clear, 1, 1, 1);
                pressure.GetData(state);
                Assert.That(math.asfloat(state[0].x), Is.EqualTo(3f));

                state[0] = new uint4(math.asuint(.984375f), 60, 1400, 376);
                state[1] = new uint4(0, 0, math.asuint(1f), math.asuint(1f));
                state[2] = new uint4(0, 0, 0, 700);
                pressure.SetData(state);
                shader.Dispatch(clear, 1, 1, 1);
                pressure.GetData(state);
                Assert.That(math.asfloat(state[0].x), Is.EqualTo(1f));
                state[0].z = 700;
                state[0].w = 0;
                pressure.SetData(state);
                for (int frame = 0; frame < 180; frame++) shader.Dispatch(clear, 1, 1, 1);
                pressure.GetData(state);
                Assert.That(math.asfloat(state[0].x), Is.EqualTo(1f));
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 2048);
                for (int frame = 0; frame < 60; frame++) shader.Dispatch(clear, 1, 1, 1);
                pressure.GetData(state);
                Assert.That(math.asfloat(state[0].x), Is.LessThan(1f));

                shader.Dispatch(reset, 1, 1, 1);
                pressure.GetData(state);
                Assert.That(state[0], Is.EqualTo(uint4.zero));
                Assert.That(math.asfloat(state[1].z), Is.LessThan(1f));
                state[0].x = math.asuint(2f);
                pressure.SetData(state);
                shader.SetVector("_VSMReceiverQuality", new Vector4(1, 1, .1f, 1));
                shader.Dispatch(clear, 1, 1, 1);
                pressure.GetData(state);
                Assert.That(state[0], Is.EqualTo(uint4.zero));
            }
            finally { UnityEngine.Object.DestroyImmediate(shader); }
        }

        [Test]
        public void PagePressure_CoarsensDegenerateAndUnattainableFineLevels()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"));
            using var projections = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 160);
            using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 16);
            using var inputs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16);
            using var normals = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16);
            using var results = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 8);
            try
            {
                var data = new VirtualShadowMapProjection[4];
                for (int i = 0; i < data.Length; i++)
                {
                    float size = 2 << i;
                    Matrix4x4 matrix = Matrix4x4.Scale(Vector3.one / size);
                    matrix.m03 = matrix.m13 = matrix.m23 = .5f;
                    data[i].WorldToShadow = matrix;
                    data[i].Parameters = new Vector4(size / 512, 0, 0, 100);
                }
                projections.SetData(data);
                pressure.SetData(new[] { new uint4(math.asuint(2f), 0, 0, 0), uint4.zero, uint4.zero });
                inputs.SetData(new[] { Vector4.zero, Vector4.zero });
                normals.SetData(new[] { new Vector4(1, 0, 0, 0), new Vector4(1, 0, .001f, 0) });
                int kernel = shader.FindKernel("InspectPressureDensity");
                shader.SetBuffer(kernel, "_VSMProjections", projections);
                shader.SetBuffer(kernel, "_VSMPagePressure", pressure);
                shader.SetBuffer(kernel, "_SamplingInputs", inputs);
                shader.SetBuffer(kernel, "_SamplingNormals", normals);
                shader.SetBuffer(kernel, "_SamplingResults", results);
                shader.SetInt("_SamplingCount", 2);
                shader.SetInt("_VSMProjectionCount", 4);
                shader.SetInt("_VSMPrototypeVirtualResolution", 512);
                shader.SetInt("_CSMOutputWidth", 128);
                shader.SetInt("_CSMOutputHeight", 128);
                shader.SetVector("_VSMReceiverParameters", Vector4.zero);
                shader.SetMatrix("_VSMReceiverViewProjection", Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)));
                var output = new Vector2[2];
                shader.SetVector("_VSMReceiverQuality", new Vector4(1, 1, 0, 0));
                shader.Dispatch(kernel, 1, 1, 1);
                results.GetData(output);
                Assert.That(output[0].x, Is.EqualTo(0));
                Assert.That(output[1].x, Is.EqualTo(0));
                shader.SetVector("_VSMReceiverQuality", new Vector4(2, 1, 0, 0));
                shader.Dispatch(kernel, 1, 1, 1);
                results.GetData(output);
                Assert.That(output[0].x, Is.EqualTo(2));
                Assert.That(output[1].x, Is.EqualTo(2));
            }
            finally { UnityEngine.Object.DestroyImmediate(shader); }
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
        public void SMRT_AdaptiveBindingRequiresActiveRaysAndAllocatesNoManagedMemory()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            try
            {
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTAdaptiveEnabled(null, Vector4.one), Is.False);
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTAdaptiveEnabled(settings,
                    VirtualShadowMapReceiverQuality.BuildSMRTParameters(settings, .5f)), Is.False);
                settings.virtualShadowMapSMRT.value = true;
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTAdaptiveEnabled(settings,
                    VirtualShadowMapReceiverQuality.BuildSMRTParameters(settings, 0)), Is.False);
                Vector4 parameters = VirtualShadowMapReceiverQuality.BuildSMRTParameters(settings, .5f);
                settings.virtualShadowMapSMRTAdaptiveRays.value = false;
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTAdaptiveEnabled(settings, parameters), Is.False);
                settings.virtualShadowMapSMRTAdaptiveRays.value = true;
                settings.virtualShadowMapSMRTTemporalDenoise.value = false;
                settings.screenSpaceShadowDenoise.value = false;
                Assert.That(VirtualShadowMapReceiverQuality.BuildSMRTAdaptiveEnabled(settings, parameters), Is.True);
                for (int i = 0; i < 32; i++) VirtualShadowMapReceiverQuality.BuildSMRTAdaptiveEnabled(settings, parameters);
                int enabled = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                    if (VirtualShadowMapReceiverQuality.BuildSMRTAdaptiveEnabled(settings, parameters)) enabled++;
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bytes, Is.Zero);
                Assert.That(enabled, Is.EqualTo(256));
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
