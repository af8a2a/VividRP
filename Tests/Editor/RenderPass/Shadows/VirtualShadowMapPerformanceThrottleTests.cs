using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.VirtualShadowMap;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapPerformanceThrottleTests
    {
        private const string ShaderPath = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";

        [Test]
        public void DefaultsAndStablePrepare_DoNotAllocateOrEnableLegacyQuotas()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            try
            {
                Assert.That(settings.virtualShadowMapThrottleLoadBudget.value, Is.Zero);
                Assert.That(settings.virtualShadowMapThrottleHistoryWeight.value, Is.EqualTo(.9f));
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(settings).z, Is.Zero);
                settings.virtualShadowMapThrottleLoadBudget.value = 2;
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(settings).z, Is.EqualTo(1));
                for (int i = 0; i < 32; i++) VirtualShadowMapPerformanceThrottle.Prepare(settings, shader);
                var feedback = VirtualShadowMapPerformanceThrottle.Feedback;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 512; i++)
                {
                    VirtualShadowMapPerformanceThrottle.Prepare(settings, shader);
                    VirtualShadowMapReceiverQuality.BuildParameters(settings);
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bytes, Is.Zero);
                Assert.That(VirtualShadowMapPerformanceThrottle.Feedback, Is.SameAs(feedback));
                Assert.That(settings.virtualShadowMapPageUpdateBudget.value, Is.Zero);
                Assert.That(settings.virtualShadowMapRasterVertexBudget.value, Is.Zero);
                settings.virtualShadowMapThrottleLoadBudget.value = 0;
                VirtualShadowMapPerformanceThrottle.Prepare(settings, shader);
                Assert.That(VirtualShadowMapPerformanceThrottle.Enabled, Is.False);
                settings.virtualShadowMapThrottleLoadBudget.value = 1;
                Assert.That(VirtualShadowMapPerformanceThrottle.Prepare(settings, null), Is.False);
                Assert.That(VirtualShadowMapPerformanceThrottle.Enabled, Is.False);

            }
            finally { VirtualShadowMapPerformanceThrottle.Dispose(); Object.DestroyImmediate(settings); }
        }

        [Test]
        public void ProductionLifecycle_RejectsAbortedAndDisabledFeedbackAndPreservesStableBuffers()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            using var cmd = new CommandBuffer();
            try
            {
                Assert.That(VirtualShadowMapPrototypeRuntime.EnsureResources(512, 3, 128), Is.True);
                VirtualShadowMapPrototypeRuntime.Projections.EnsureCapacity(3);
                VirtualShadowMapPrototypeRuntime.Projections.Buffer.SetData(new VirtualShadowMapProjection[3]);
                VirtualShadowMapPrototypeRuntime.PagePressure.SetData(new uint4[3]);
                settings.virtualShadowMapThrottleLoadBudget.value = 1;
                settings.virtualShadowMapThrottleHistoryWeight.value = 0;
                VirtualShadowMapPerformanceThrottle.Prepare(settings, shader);
                var feedback = new uint2[25]; feedback[0] = feedback[1] = new uint2(20000, 0);
                var output = new uint4[26];
                float[] expected = { 0, .2f, .4f, 0, 0 };
                for (int frame = 0; frame < expected.Length; frame++)
                {
                    if (frame == 4)
                    {
                        VirtualShadowMapPerformanceThrottle.Disable();
                        VirtualShadowMapPerformanceThrottle.Prepare(settings, shader);
                    }
                    VirtualShadowMapPerformanceThrottle.Feedback.SetData(feedback);
                    cmd.Clear();
                    VirtualShadowMapPerformanceThrottle.BeginFrame(cmd, frame, frame < 2 ? 1ul : 2ul,
                        3, new Vector4(1, 0, 1, 0));
                    VirtualShadowMapPerformanceThrottle.CompleteFrame(frame != 2);
                    Graphics.ExecuteCommandBuffer(cmd);
                    VirtualShadowMapPerformanceThrottle.Current.GetData(output);
                    Assert.That(math.asfloat(output[1].x), Is.EqualTo(expected[frame]).Within(1e-6));
                    if (frame == 2) Assert.That(output[2].x, Is.Zero); // identity remap, but retain total load
                }
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.ReleaseResources();
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(1)]
        [TestCase(17)]
        [TestCase(24)]
        public void GPUThrottle_MatchesUEColdLoadWarmRecoveryAndMissingEntryHistory(int levels)
        {
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            using var previous = new GraphicsBuffer(GraphicsBuffer.Target.Structured, levels + 2, 16);
            using var current = new GraphicsBuffer(GraphicsBuffer.Target.Structured, levels + 2, 16);
            using var feedback = new GraphicsBuffer(GraphicsBuffer.Target.Structured, levels + 1, 8);
            using var projection = new GraphicsBuffer(GraphicsBuffer.Target.Structured, levels, 160);
            using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 16);
            try
            {
                var state = new uint4[levels + 2];
                var counts = new uint2[levels + 1];
                var projections = new VirtualShadowMapProjection[levels];
                var intensities = new float[levels];
                float global = 0;
                int process = shader.FindKernel("VSMProcessPreviousPerformance");
                int update = shader.FindKernel("VSMUpdatePerformanceThrottle");
                shader.SetInt("_VSMProjectionCount", levels);
                shader.SetBuffer(process, "_VSMPrevThrottle", previous);
                shader.SetBuffer(process, "_VSMThrottleRW", current);
                shader.SetBuffer(process, "_VSMRasterFeedbackRW", feedback);
                shader.SetBuffer(update, "_VSMPrevThrottle", previous);
                shader.SetBuffer(update, "_VSMThrottleRW", current);
                shader.SetBuffer(update, "_VSMThrottleProjectionsRW", projection);
                shader.SetBuffer(update, "_VSMPagePressure", pressure);
                for (int frame = 0; frame < 128; frame++)
                {
                    bool valid = frame != 0 && frame != 124; // cold and re-enabled frames
                    bool entriesValid = frame != 8; // camera/light identity changes
                    float timeStrength = frame >= 120 ? .65f : -1; // time overrides load
                    float memory = frame == 121 ? 2 : .5f;
                    uint maxHW = 0;
                    counts[0] = 0;
                    for (int i = 0; i < levels; i++)
                    {
                        uint hw = frame < 20 || frame >= 120 ? (uint)(20000 / (i + 1)) : 0;
                        counts[i + 1] = new uint2(hw, 0);
                        counts[0] += counts[i + 1];
                        if (entriesValid) maxHW = Math.Max(maxHW, hw);
                        projections[i].Parameters = new Vector4(.25f, 1, 0, 100);
                        projections[i].WorldToClip = Matrix4x4.identity;
                    }
                    previous.SetData(state); feedback.SetData(counts); projection.SetData(projections);
                    pressure.SetData(new[] { new uint4(math.asuint(memory), 0, 0, 0), uint4.zero, uint4.zero });
                    shader.SetInt("_VSMThrottlePreviousValid", valid ? 1 : 0);
                    shader.SetInt("_VSMThrottleEntriesValid", entriesValid ? 1 : 0);
                    shader.SetVector("_VSMReceiverQuality", new Vector4(2, 0, 1, 0));
                    shader.SetVector("_VSMThrottleParameters", new Vector4(1, .9f, 99999, timeStrength));
                    shader.Dispatch(process, 1, 1, 1);
                    shader.Dispatch(update, 1, 1, 1);
                    current.GetData(state); projection.GetData(projections);
                    global = !valid ? 0 : timeStrength >= 0 ? timeStrength
                        : Mathf.Clamp01(global + Mathf.Clamp((counts[0].x * .00012176f - 1) * .5f, -.01f, .2f));
                    Assert.That(math.asfloat(state[1].x), Is.EqualTo(global).Within(2e-6), "Global frame " + frame);
                    for (int i = 0; i < levels; i++)
                    {
                        uint hw = valid && entriesValid ? counts[i + 1].x : 0;
                        float old = valid && entriesValid ? intensities[i] : 0;
                        intensities[i] = hw == 0 ? global : Mathf.Lerp(Mathf.Clamp01((float)hw / maxHW * global), old, .9f);
                        float bias = intensities[i] * (2 - memory);
                        Assert.That(math.asfloat(state[i + 2].z), Is.EqualTo(intensities[i]).Within(2e-6), "Entry " + i);
                        Assert.That(projections[i].Parameters.z, Is.EqualTo(bias).Within(3e-6));
                        Assert.That(projections[i].Parameters.x, Is.EqualTo(.25f));
                        Assert.That(projections[i].WorldToClip, Is.EqualTo(Matrix4x4.identity));
                    }
                    feedback.GetData(counts);
                    foreach (var count in counts) Assert.That(count, Is.EqualTo(uint2.zero));
                    if (frame == 119) Assert.That(global, Is.EqualTo(0).Within(2e-6)); // full warm-cache recovery
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [Test]
        public void GPURasterFeedback_CountsSubmittedWindowsAndOverflowInstancesAcrossFourPasses()
        {
            int lists = (int)VividRendererListID.Count;
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, lists * 2 * 4, 4);
            using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 32, 16);
            using var rasterPages = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            using var feedback = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 8);
            using var dispatch = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 3, 4);
            try
            {
                var indirect = new uint[lists * 2 * 4];
                indirect[0] = indirect[4] = indirect[lists * 4] = 384;
                indirect[1] = 2; indirect[3] = 0;
                indirect[5] = 5; indirect[7] = 8; // unsent material list, must not count
                indirect[lists * 4 + 1] = 21; indirect[lists * 4 + 3] = 32; // 3 records x 7 raster instances
                args.SetData(indirect);
                var data = new uint4[32];
                data[0] = new uint4(0, 0, 0, 0x80004400); // level 0 tagged window
                data[1] = new uint4(0, 0, 16, 1); // level 1 single page
                data[31] = new uint4(0, 0, 0, 15);
                data[30] = new uint4(0, 0, 16, 31);
                data[29] = new uint4(0, 0, 32, 47);
                requests.SetData(data); rasterPages.SetData(new uint[] { 7 }); feedback.SetData(new uint2[4]);
                shader.SetInt("_VSMPrototypePagesPerAxis", 4);
                shader.SetInt("_VSMRasterFeedbackDrawMask", 1 | (1 << lists));
                int prepare = shader.FindKernel("VSMPrepareRasterFeedback"), capture = shader.FindKernel("VSMCaptureRasterFeedback");
                foreach (int kernel in new[] { prepare, capture })
                {
                    shader.SetBuffer(kernel, "_VSMPrototypeMeshletPageIndirectArgs", args);
                    shader.SetBuffer(kernel, "_VSMPrototypeMeshletRasterPages", rasterPages);
                    shader.SetBuffer(kernel, "_VSMRasterFeedbackRW", feedback);
                }
                shader.SetBuffer(prepare, "_VSMRasterFeedbackArgs", dispatch);
                shader.SetBuffer(capture, "_VSMPrototypeMeshletPageRequests", requests);
                for (int pass = 1; pass <= 4; pass++)
                {
                    shader.Dispatch(prepare, 1, 1, 1);
                    shader.DispatchIndirect(capture, dispatch, 0);
                    var result = new uint2[4]; feedback.GetData(result);
                    CollectionAssert.AreEqual(new[] { new uint2((uint)(23 * pass), 0), new uint2((uint)(8 * pass), 0),
                        new uint2((uint)(8 * pass), 0), new uint2((uint)(7 * pass), 0) }, result);
                }
                shader.SetInt("_VSMRasterFeedbackDrawMask", 0);
                shader.Dispatch(prepare, 1, 1, 1);
                var groups = new uint[3]; dispatch.GetData(groups);
                Assert.That(groups[0], Is.Zero);
                var unchanged = new uint2[4]; feedback.GetData(unchanged);
                Assert.That(unchanged[0].x, Is.EqualTo(92));
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [Test]
        public void GPUSelection_UsesUnbiasedTargetEntryAndPressureOnlyForRequests()
        {
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"));
            using var projection = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 5, 160);
            using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 16);
            using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 16);
            using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 8);
            try
            {
                var data = new VirtualShadowMapProjection[5];
                for (int i = 0; i < data.Length; i++)
                {
                    data[i].SelectionSphere = new Vector4(0, 0, 0, -2.56f * (1 << i));
                    data[i].Parameters.z = i == 1 ? 1 : 0;
                }
                projection.SetData(data);
                pressure.SetData(new[] { new uint4(math.asuint(1f), 0, 0, 0), uint4.zero, uint4.zero });
                input.SetData(new[] { new Vector4(.64f, 0, 0, 0), new Vector4(1.28f, 0, 0, 0), new Vector4(20.48f, 0, 0, 0) });
                int kernel = shader.FindKernel("InspectUEClipmapSelection");
                shader.SetBuffer(kernel, "_VSMProjections", projection); shader.SetBuffer(kernel, "_VSMPagePressure", pressure);
                shader.SetBuffer(kernel, "_SamplingInputs", input); shader.SetBuffer(kernel, "_SamplingResults", output);
                shader.SetInt("_SamplingCount", 3); shader.SetInt("_VSMProjectionCount", 5);
                shader.SetVector("_VSMReceiverQuality", new Vector4(2, 1, 1, 0));
                shader.Dispatch(kernel, 1, 1, 1);
                var result = new float2[3]; output.GetData(result);
                CollectionAssert.AreEqual(new[] { new float2(1, 3), new float2(3, 3), new float2(-1, -1) }, result);
                shader.SetVector("_VSMReceiverQuality", new Vector4(2, 1, 0, 0));
                shader.Dispatch(kernel, 1, 1, 1); output.GetData(result);
                CollectionAssert.AreEqual(new[] { new float2(1, 2), new float2(2, 3), new float2(-1, -1) }, result);
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
