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
                var clusterCounts = VirtualShadowMapPerformanceThrottle.ClusterCounts;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 512; i++)
                {
                    VirtualShadowMapPerformanceThrottle.Prepare(settings, shader);
                    VirtualShadowMapReceiverQuality.BuildParameters(settings);
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bytes, Is.Zero);
                Assert.That(VirtualShadowMapPerformanceThrottle.Feedback, Is.SameAs(feedback));
                Assert.That(VirtualShadowMapPerformanceThrottle.ClusterCounts, Is.SameAs(clusterCounts));
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
        public void GPURasterFeedback_CountsCulledClustersAcrossFourPassesAndResetsScratch()
        {
            int lists = (int)VividRendererListID.Count, levels = VirtualShadowMapClipmapLayout.MaxLevels;
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            using var counts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VirtualShadowMapPerformanceThrottle.ClusterCountEntries, 4);
            using var feedback = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 8);
            try
            {
                var clusters = new uint[counts.count];
                clusters[0] = 2; clusters[1] = 1;
                clusters[levels] = 999; // Unsent material list must not contribute.
                clusters[lists * levels] = 5; clusters[lists * levels + 1] = 9; clusters[lists * levels + 2] = 6;
                feedback.SetData(new uint2[4]);
                shader.SetInt("_VSMProjectionCount", 3);
                shader.SetInt("_VSMRasterFeedbackDrawMask", 1 | (1 << lists));
                int prepare = shader.FindKernel("VSMPrepareRasterFeedback"), capture = shader.FindKernel("VSMCaptureRasterFeedback");
                shader.SetBuffer(prepare, "_VSMRasterClusterCountsRW", counts);
                shader.SetBuffer(capture, "_VSMRasterClusterCountsRW", counts);
                shader.SetBuffer(capture, "_VSMRasterFeedbackRW", feedback);
                for (int pass = 1; pass <= 4; pass++)
                {
                    counts.SetData(clusters);
                    shader.Dispatch(capture, 1, 1, 1);
                    var result = new uint2[4]; feedback.GetData(result);
                    CollectionAssert.AreEqual(new[] { new uint2((uint)(23 * pass), 0), new uint2((uint)(7 * pass), 0),
                        new uint2((uint)(10 * pass), 0), new uint2((uint)(6 * pass), 0) }, result);
                    shader.Dispatch(prepare, (counts.count + 63) / 64, 1, 1);
                    var cleared = new uint[counts.count]; counts.GetData(cleared);
                    Assert.That(cleared, Is.EqualTo(new uint[counts.count]));
                    shader.Dispatch(capture, 1, 1, 1); feedback.GetData(result);
                    Assert.That(result[0].x, Is.EqualTo(23 * pass), "Empty post pass must not reuse main counts");
                }
                counts.SetData(clusters); shader.SetInt("_VSMRasterFeedbackDrawMask", 0);
                shader.Dispatch(capture, 1, 1, 1);
                var unchanged = new uint2[4]; feedback.GetData(unchanged);
                Assert.That(unchanged[0].x, Is.EqualTo(92));
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(0, 7, false)]
        [TestCase(1, 7, false)]
        [TestCase(4, 7, false)]
        [TestCase(5, 7, false)]
        [TestCase(9, 37, false)]
        [TestCase(9, 91, false)]
        [TestCase(9, 37, true)]
        public void GPUClusterWindows_CountNonemptyWindowsWithoutOverflowPadding(int activeWindows, int fanout, bool dynamic)
        {
            const int axis = 12, level = 2, pages = axis * axis * 3;
            int lists = (int)VividRendererListID.Count;
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"));
            using var counts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VirtualShadowMapPerformanceThrottle.ClusterCountEntries, 4);
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 4);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 16);
            using var masks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 8);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, lists * 2 * 4, 4);
            using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16, 16);
            using var raster = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            using var inputs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16);
            using var outputs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 8);
            try
            {
                var entries = new uint[pages]; var state = new uint4[pages]; var mask = new uint2[pages];
                for (int i = 0; i < 9; i++)
                {
                    int page = level * axis * axis + i / 3 * 4 * axis + i % 3 * 4;
                    // One candidate in each window. Clean pages, holes and deferred
                    // pages must not create cluster records; static ignores masks.
                    entries[page] = i < activeWindows ? 0xc0000000u : 0u;
                    state[page] = new uint4(i < activeWindows ? 2u | 4u | 32768u : 2u | 8u, 1, 0, 0);
                    mask[page] = new uint2(i % 2 == 0 ? uint.MaxValue : 0u, 0);
                    entries[page + 1] = 0xc0000000u;
                    state[page + 1] = new uint4(2u | 4u | 32768u | 131072u, 1, 0, 0);
                }
                table.SetData(entries); metadata.SetData(state); masks.SetData(mask);
                var indirect = new uint[lists * 2 * 4]; indirect[lists * 4 + 3] = 16;
                args.SetData(indirect); counts.SetData(new uint[counts.count]); raster.SetData(new uint[] { (uint)fanout });
                inputs.SetData(new[] { new Vector4(0, 0, 11, 11), new Vector4(0, 0, 1535, 1535) });
                int kernel = shader.FindKernel("InspectVSMClusterWindowFeedback");
                shader.SetInt("_VSMPrototypePagesPerAxis", axis); shader.SetInt("_VSMPrototypePageSize", 128);
                shader.SetInt("_VSMPrototypePageTableEntryCount", pages); shader.SetInt("_VSMPrototypeCasterLayer", dynamic ? 1 : 0);
                shader.SetInt("_VSMReceiverMaskEnabled", 1); shader.SetInts("_SamplingPixel", level, 0);
                shader.SetBuffer(kernel, "_VSMPrototypePageTable", table); shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(kernel, "_VSMPageReceiverMasks", masks); shader.SetBuffer(kernel, "_VSMRasterClusterCountsRW", counts);
                shader.SetBuffer(kernel, "_VSMPrototypeMeshletPageIndirectArgs", args);
                shader.SetBuffer(kernel, "_VSMPrototypeMeshletPageRequests", requests); shader.SetBuffer(kernel, "_VSMPrototypeMeshletRasterPages", raster);
                shader.SetBuffer(kernel, "_SamplingInputs", inputs); shader.SetBuffer(kernel, "_SamplingResults", outputs);
                shader.Dispatch(kernel, 1, 1, 1);
                int expected = dynamic ? (activeWindows + 1) / 2 : activeWindows;
                var actual = new uint[counts.count]; counts.GetData(actual);
                var expectedCounts = new uint[counts.count];
                expectedCounts[(expected > 4 ? lists * VirtualShadowMapClipmapLayout.MaxLevels : 0) + level] = (uint)expected;
                Assert.That(actual, Is.EqualTo(expectedCounts));
                args.GetData(indirect);
                Assert.That(indirect[1], Is.EqualTo(expected <= 4 ? expected : 0));
                Assert.That(indirect[lists * 4 + 1], Is.EqualTo(expected > 4 ? fanout : 0));
                var result = new float2[1]; outputs.GetData(result);
                Assert.That(result[0], Is.EqualTo(new float2(expected <= 4 ? 1 : 0, expected)));
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
