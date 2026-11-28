using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.VirtualShadowMap;

namespace VividRP.Tests.Editor.RenderPass.Shadows
{
    public sealed class VirtualShadowMapScaleAndDebugPhase4Tests
    {
        private const string ResolveShaderPath = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";
        private const uint PageAllocatedBit = 1u << 1;
        private const uint PageCachedBit = 1u << 3;
        private const uint PageDynamicDirtyBit = 1u << 15;
        private const uint PageRequestedBit = 1u << 0;

        private static ComputeShader LoadResolveShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ResolveShaderPath);
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute");
            return shader;
        }

        [Test]
        public void ScaleOut36PointLights_PrioritizesClosestFullAndDowngradesFarToSinglePage_ZeroGC()
        {
            const int lightCount = 36;
            var lightData = new VividLightData
            {
                punctualLightCount = lightCount,
                punctualLights = new VividLightData.PunctualLightData[lightCount],
                punctualLightShadowMeta = new VividLightData.PunctualLightShadowMeta[lightCount],
            };

            // Place farthest lights FIRST in the input array and closest lights LAST to verify priority sorting.
            for (int i = 0; i < lightCount; i++)
            {
                float distance = 120.0f - i * 3.0f; // i=0 -> 120m (far), i=35 -> 15m (closest)
                lightData.punctualLights[i] = new VividLightData.PunctualLightData
                {
                    positionWS = new Vector3(0f, 2f, distance),
                    range = 10f,
                    color = Vector3.one,
                    lightType = 0u, // Point
                    directionWS = Vector3.down,
                    upWS = Vector3.forward,
                    shadowStrength = 1.0f,
                };
                lightData.punctualLightShadowMeta[i] = new VividLightData.PunctualLightShadowMeta
                {
                    lightEntityId = EntityId.FromULong((ulong)(1000 + i)),
                    spotAngle = 90f,
                    shapeRadius = 0.25f,
                    flags = VividLightRenderDataFlags.CastShadows,
                };
            }

            using var grid = new VirtualShadowMapLocalLightGrid();
            var registry = new VirtualShadowMapRegistry();

            int collected = grid.CollectLocalLights(lightData, Vector3.zero, 60f, 1080, 4096, 0f, 128f, true);
            Assert.That(collected, Is.EqualTo(lightCount));

            registry.PrepareFrame(10ul, null, grid.CollectedDescriptors, collected, maxDistantPagesPerFrame: 8);
            registry.CommitRecordedFrame();

            // 64 Full VSM cap / 6 faces per Point = at most 10 Full Point lights (60 faces); remaining 26 are Single-Page (156 faces).
            Assert.That(registry.FullLocalCount, Is.LessThanOrEqualTo(VirtualShadowMapLocalLightGrid.MaxFullLocalVSMs));
            Assert.That(registry.FullLocalCount, Is.EqualTo(60));
            Assert.That(registry.SinglePageCount, Is.EqualTo(26 * 6));
            Assert.That(registry.ProjectionCount, Is.EqualTo(lightCount * 6));

            // Closest lights (highest indices in input array) must be the ones assigned Full VSMs.
            for (int i = 0; i < 10; i++)
            {
                Assert.That(grid.CollectedDescriptors[i].IsSinglePage, Is.False, $"Top-{i} closest light must get Full VSM.");
                Assert.That(grid.CollectedDescriptors[i].PunctualLightIndex, Is.EqualTo((uint)(lightCount - 1 - i)));
            }
            for (int i = 10; i < lightCount; i++)
            {
                Assert.That(grid.CollectedDescriptors[i].IsSinglePage, Is.True, $"Overflow/distant light {i} must downgrade to Single-Page.");
            }

            // Steady-state zero managed GC allocation check (warm up past full Round-Robin wrap).
            for (int w = 0; w < 24; w++)
            {
                int n = grid.CollectLocalLights(lightData, Vector3.zero, 60f, 1080, 4096, 0f, 128f, true);
                registry.PrepareFrame(10ul, null, grid.CollectedDescriptors, n, maxDistantPagesPerFrame: 8);
                registry.CommitRecordedFrame();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 64; f++)
            {
                int n = grid.CollectLocalLights(lightData, Vector3.zero, 60f, 1080, 4096, 0f, 128f, true);
                registry.PrepareFrame(10ul, null, grid.CollectedDescriptors, n, maxDistantPagesPerFrame: 8);
                registry.CommitRecordedFrame();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void RoundRobinDistantRefresh_RespectsPerFrameBudgetAndCyclesAllFaces()
        {
            const int lightCount = 12;
            var descriptors = new VividVSMLocalLightDescriptor[lightCount];
            for (int i = 0; i < lightCount; i++)
            {
                descriptors[i] = new VividVSMLocalLightDescriptor
                {
                    LightEntityId = (ulong)(2000 + i),
                    LightType = VividVSMLightType.Spot,
                    PositionWS = new Vector3(i * 5f, 3f, 80f),
                    Rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward),
                    Range = 8f,
                    SpotAngle = 60f,
                    MaxResolution = 4096,
                    IsSinglePage = true,
                    PunctualLightIndex = (uint)i,
                };
            }

            var registry = new VirtualShadowMapRegistry();
            // Frame 1: initial population (no previous cached records to refresh).
            registry.PrepareFrame(20ul, null, descriptors, lightCount, maxDistantPagesPerFrame: 4);
            Assert.That(registry.SinglePageCount, Is.EqualTo(lightCount));
            Assert.That(registry.DistantRefreshedThisFrame, Is.EqualTo(0));
            registry.CommitRecordedFrame();

            // Frames 2..4: 4 distinct distant faces refreshed per frame -> all 12 covered in 3 frames.
            var refreshedMask = new HashSet<int>();
            for (int frame = 0; frame < 3; frame++)
            {
                registry.PrepareFrame(20ul, null, descriptors, lightCount, maxDistantPagesPerFrame: 4);
                Assert.That(registry.DistantRefreshedThisFrame, Is.EqualTo(4));
                int invalidatedThisFrame = 0;
                int validThisFrame = 0;
                for (int i = 0; i < lightCount; i++)
                {
                    bool valid = (registry.NextMap[i].Flags & (uint)VividVSMNextMapFlags.Valid) != 0u;
                    if (valid)
                    {
                        validThisFrame++;
                    }
                    else
                    {
                        invalidatedThisFrame++;
                        refreshedMask.Add(i);
                    }
                }
                Assert.That(invalidatedThisFrame, Is.EqualTo(4));
                Assert.That(validThisFrame, Is.EqualTo(8));
                registry.CommitRecordedFrame();
            }
            Assert.That(refreshedMask.Count, Is.EqualTo(lightCount), "Round-Robin must visit every distant Single-Page face.");

            // Budget = 0 disables periodic forced refresh.
            registry.PrepareFrame(20ul, null, descriptors, lightCount, maxDistantPagesPerFrame: 0);
            Assert.That(registry.DistantRefreshedThisFrame, Is.EqualTo(0));
            for (int i = 0; i < lightCount; i++)
                Assert.That(registry.NextMap[i].Flags & (uint)VividVSMNextMapFlags.Valid, Is.Not.Zero);
        }

        [Test]
        public void GPU_LocalPagePressure_RampsUpOnHighLoadAndRecoversAfterHysteresis()
        {
            if (!VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform())
                Assert.Ignore("VSM compute tests require Reverse-Z and compute shader support.");

            ComputeShader shader = LoadResolveShader();
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("VSMUpdateLocalPagePressure");
            Assert.That(kernel, Is.GreaterThanOrEqualTo(0));

            using var counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, sizeof(uint));
            using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint) * 4);
            pressure.SetData(new uint4[3]);

            void DispatchFrame(int frameIndex, uint requested, uint overflow, uint resident)
            {
                counters.SetData(new uint[] { resident, requested, 0u, overflow, resident, 0u, 0u, 0u });
                using var cmd = new CommandBuffer();
                cmd.SetComputeIntParam(shader, "_VSMPrototypePhysicalPageCapacity", 256);
                cmd.SetComputeIntParam(shader, "_VSMPrototypeFeedbackFrameIndex", frameIndex);
                cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.LocalPagePressureEnabledId, 1);
                cmd.SetComputeBufferParam(shader, kernel, "_VSMPrototypeAllocatorCounters", counters);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapReceiverQuality.PressureRWId, pressure);
                cmd.DispatchCompute(shader, kernel, 1, 1, 1);
                Graphics.ExecuteCommandBuffer(cmd);
            }

            // 1. Over-subscribed frames (250 requested + 12 overflow out of 256) -> bias must ramp up.
            DispatchFrame(1, 250u, 12u, 256u);
            DispatchFrame(2, 250u, 12u, 256u);
            var read1 = new uint4[3];
            pressure.GetData(read1);
            float highBias = math.asfloat(read1[0].x);
            Assert.That(highBias, Is.GreaterThan(0.25f));
            Assert.That(highBias, Is.LessThanOrEqualTo(4.0f));

            // 2. Low-load frames during 10-frame hysteresis window (frames 3..12) -> bias stays pinned.
            for (int f = 3; f <= 12; f++)
                DispatchFrame(f, 64u, 0u, 64u);
            var readHold = new uint4[3];
            pressure.GetData(readHold);
            Assert.That(math.asfloat(readHold[0].x), Is.EqualTo(highBias).Within(1e-4f));

            // 3. Past 10 frames below 85% (frames 13..20) -> bias smoothly recovers downward.
            for (int f = 13; f <= 20; f++)
                DispatchFrame(f, 64u, 0u, 64u);
            var readRecover = new uint4[3];
            pressure.GetData(readRecover);
            Assert.That(math.asfloat(readRecover[0].x), Is.LessThan(highBias));
        }

        [Test]
        public void GPU_UpdatePhysicalPagesUnified_SkipsDynamicDirtyForSinglePageDistantLight()
        {
            if (!VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform())
                Assert.Ignore("VSM compute tests require Reverse-Z and compute shader support.");

            ComputeShader shader = LoadResolveShader();
            Assert.That(shader, Is.Not.Null);
            int kReset = shader.FindKernel("VSMResetPhysicalPageListsUE");
            int kUpdate = shader.FindKernel("VSMUpdatePhysicalPagesUnified");
            Assert.That(kUpdate, Is.GreaterThanOrEqualTo(0));

            var descriptors = new[]
            {
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 3001ul,
                    LightType = VividVSMLightType.Spot,
                    PositionWS = new Vector3(0f, 4f, 0f),
                    Rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward),
                    Range = 12f,
                    SpotAngle = 60f,
                    MaxResolution = 256,
                    IsSinglePage = false,
                    UseReceiverMask = true,
                    PunctualLightIndex = 0u,
                },
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 3002ul,
                    LightType = VividVSMLightType.Spot,
                    PositionWS = new Vector3(0f, 4f, 200f),
                    Rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward),
                    Range = 8f,
                    SpotAngle = 60f,
                    MaxResolution = 256,
                    IsSinglePage = true,
                    UseReceiverMask = false,
                    PunctualLightIndex = 1u,
                },
            };

            var registry = new VirtualShadowMapRegistry();
            registry.PrepareFrame(30ul, null, descriptors, descriptors.Length);
            int totalEntries = registry.TotalPageTableEntries;
            const int capacity = 4;

            uint fullPageIdx = registry.Projections[0].Addressing.x;
            uint singlePageIdx = registry.Projections[1].Addressing.x;

            using var projBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, registry.ProjectionCount, VirtualShadowMapProjection.Stride);
            using var pageLists = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 4 + 4, sizeof(uint));
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            using var descs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint) * 2);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint) * 4);
            using var pageTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint));
            using var reqFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint));
            using var counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, sizeof(uint));
            using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint) * 4);

            var projUpload = new VirtualShadowMapProjection[registry.ProjectionCount];
            Array.Copy(registry.Projections, projUpload, registry.ProjectionCount);
            projBuf.SetData(projUpload);

            var listData = new uint[capacity * 4 + 4];
            for (int s = 0; s < capacity; s++) listData[s] = (uint)s;
            pageLists.SetData(listData);

            owners.SetData(new uint[] { fullPageIdx + 1u, singlePageIdx + 1u, 0u, 0u });
            descs.SetData(new[]
            {
                VirtualShadowMapAddressing.EncodePhysicalOwnerDesc(0u, 0u, uint2.zero, VividVSMOwnerFlags.Valid),
                VirtualShadowMapAddressing.EncodePhysicalOwnerDesc(1u, 0u, uint2.zero, VividVSMOwnerFlags.Valid | VividVSMOwnerFlags.SinglePage),
                uint2.zero,
                uint2.zero,
            });

            var metaData = new uint4[totalEntries];
            metaData[fullPageIdx] = new uint4(PageAllocatedBit | PageCachedBit, 1u, 5u, 0u);
            metaData[singlePageIdx] = new uint4(PageAllocatedBit | PageCachedBit, 2u, 5u, 0u);
            metadata.SetData(metaData);

            var reqData = new uint[totalEntries];
            reqData[fullPageIdx] = PageRequestedBit;
            reqData[singlePageIdx] = PageRequestedBit;
            reqFlags.SetData(reqData);
            pageTable.SetData(new uint[totalEntries]);
            counters.SetData(new uint[8]);
            pressure.SetData(new uint4[3]);

            using (var cmd = new CommandBuffer())
            {
                cmd.SetComputeIntParam(shader, "_VSMPrototypePhysicalPageCapacity", capacity);
                cmd.SetComputeIntParam(shader, "_VSMPrototypeFeedbackFrameIndex", 6);
                cmd.SetComputeIntParam(shader, VirtualShadowMapPrototypeRuntime.ReceiverMaskEnabledId, 1);
                cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.CountId, registry.ProjectionCount);

                cmd.SetComputeBufferParam(shader, kReset, VirtualShadowMapPrototypeRuntime.PhysicalPageListsId, pageLists);
                cmd.SetComputeBufferParam(shader, kReset, "_VSMPrototypeAllocatorCounters", counters);
                cmd.SetComputeBufferParam(shader, kReset, VirtualShadowMapReceiverQuality.PressureRWId, pressure);
                cmd.DispatchCompute(shader, kReset, 1, 1, 1);

                cmd.SetComputeBufferParam(shader, kUpdate, VirtualShadowMapProjectionSet.BufferId, projBuf);
                cmd.SetComputeBufferParam(shader, kUpdate, VirtualShadowMapPrototypeRuntime.PhysicalPageListsId, pageLists);
                cmd.SetComputeBufferParam(shader, kUpdate, "_VSMPrototypePhysicalPageOwners", owners);
                cmd.SetComputeBufferParam(shader, kUpdate, VirtualShadowMapPrototypeRuntime.PhysicalPageDescriptorsId, descs);
                cmd.SetComputeBufferParam(shader, kUpdate, "_VSMPrototypePageMetadata", metadata);
                cmd.SetComputeBufferParam(shader, kUpdate, "_VSMPrototypeWritablePageTable", pageTable);
                cmd.SetComputeBufferParam(shader, kUpdate, "_VSMPageRequestFlagsRO", reqFlags);
                cmd.SetComputeBufferParam(shader, kUpdate, "_VSMPrototypeAllocatorCounters", counters);
                cmd.DispatchCompute(shader, kUpdate, 1, 1, 1);
                Graphics.ExecuteCommandBuffer(cmd);
            }

            var metaOut = new uint4[totalEntries];
            metadata.GetData(metaOut);
            Assert.That((metaOut[fullPageIdx].x & PageDynamicDirtyBit) != 0u, Is.True,
                "Full local VSM page must mark DynamicDirty when requested with ReceiverMask enabled.");
            Assert.That((metaOut[singlePageIdx].x & PageDynamicDirtyBit) == 0u, Is.True,
                "Single-Page distant VSM (SkipDynamicInvalid) must remain cached without per-frame DynamicDirty.");
            Assert.That((metaOut[singlePageIdx].x & PageCachedBit) != 0u, Is.True);
        }

        [Test]
        public void GPU_LocalPerformanceThrottle_AppliesPerVSMBiasOnlyToHeavyLocalViews()
        {
            if (!VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform())
                Assert.Ignore("VSM compute tests require Reverse-Z and compute shader support.");

            ComputeShader shader = LoadResolveShader();
            Assert.That(shader, Is.Not.Null);
            int kProc = shader.FindKernel("VSMProcessLocalPerformance");
            int kUpd = shader.FindKernel("VSMUpdateLocalPerformanceThrottle");
            Assert.That(kProc, Is.GreaterThanOrEqualTo(0));
            Assert.That(kUpd, Is.GreaterThanOrEqualTo(0));

            const int projCount = 3; // 0: Heavy Full Spot, 1: Light Full Spot, 2: Single-Page Spot
            var projs = new VirtualShadowMapProjection[projCount];
            projs[0].Meta = new uint4((uint)VividVSMLightType.Spot, 0u, 0u, 0u);
            projs[1].Meta = new uint4((uint)VividVSMLightType.Spot, 0u, 0u, 0u);
            projs[2].Meta = new uint4((uint)VividVSMLightType.Spot, (uint)VividVSMProjectionFlags.SinglePage, 0u, 0u);

            using var projBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, projCount, VirtualShadowMapProjection.Stride);
            using var prevThrottle = new GraphicsBuffer(GraphicsBuffer.Target.Structured, projCount + 2, 16);
            using var curThrottle = new GraphicsBuffer(GraphicsBuffer.Target.Structured, projCount + 2, 16);
            using var clusterCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, projCount, sizeof(uint));
            using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 16);

            projBuf.SetData(projs);
            prevThrottle.SetData(new uint4[projCount + 2]);
            curThrottle.SetData(new uint4[projCount + 2]);
            clusterCounts.SetData(new uint[] { 50000u, 500u, 50000u }); // Heavy load >> budget
            pressure.SetData(new uint4[3]);

            using (var cmd = new CommandBuffer())
            {
                cmd.SetComputeIntParam(shader, "_VSMThrottlePreviousValid", 1);
                cmd.SetComputeIntParam(shader, "_VSMThrottleEntriesValid", 1);
                cmd.SetComputeIntParam(shader, "_VSMLocalThrottleStartVSMId", 0);
                cmd.SetComputeIntParam(shader, "_VSMLocalThrottleCount", projCount);
                cmd.SetComputeIntParam(shader, "_VSMLocalThrottleMemoryPressureEnabled", 0);
                cmd.SetComputeVectorParam(shader, "_VSMThrottleParameters", new Vector4(1.0f, 0.0f, 4.0f, -1.0f));

                cmd.SetComputeBufferParam(shader, kProc, "_VSMPrevThrottle", prevThrottle);
                cmd.SetComputeBufferParam(shader, kProc, "_VSMThrottleRW", curThrottle);
                cmd.SetComputeBufferParam(shader, kProc, "_VSMLocalRasterClusterCountsRW", clusterCounts);
                cmd.DispatchCompute(shader, kProc, 1, 1, 1);

                cmd.SetComputeBufferParam(shader, kUpd, "_VSMPrevThrottle", prevThrottle);
                cmd.SetComputeBufferParam(shader, kUpd, "_VSMThrottleRW", curThrottle);
                cmd.SetComputeBufferParam(shader, kUpd, "_VSMThrottleProjectionsRW", projBuf);
                cmd.SetComputeBufferParam(shader, kUpd, VirtualShadowMapReceiverQuality.PressureId, pressure);
                cmd.DispatchCompute(shader, kUpd, 1, 1, 1);
                Graphics.ExecuteCommandBuffer(cmd);
            }

            var outProjs = new VirtualShadowMapProjection[projCount];
            projBuf.GetData(outProjs);
            Assert.That(outProjs[0].Parameters.z, Is.GreaterThan(outProjs[1].Parameters.z),
                "Heavier local VSM must receive strictly larger ResolutionLodBias throttle than lighter VSM.");
            Assert.That(outProjs[0].Parameters.z, Is.GreaterThan(0.0f));
            Assert.That(outProjs[2].Parameters.z, Is.EqualTo(0.0f),
                "Single-Page distant VSM must not receive mip-chain ResolutionLodBias throttling.");

            // Cluster counts must be cleared to 0 after VSMProcessLocalPerformance consumes them.
            var cleared = new uint[projCount];
            clusterCounts.GetData(cleared);
            Assert.That(cleared[0], Is.EqualTo(0u));
            Assert.That(cleared[1], Is.EqualTo(0u));
        }

        [Test]
        public void DebugPasses_NormalizeLocalModesAndCompileLocalPass()
        {
            var debugPass = new VSMDebugPass();
            try
            {
                debugPass.LightScope = VSMDebugLightScope.Local;
                Assert.That(debugPass.LightScope, Is.EqualTo(VSMDebugLightScope.Local));
                debugPass.LocalVSMIndex = -10;
                Assert.That(debugPass.LocalVSMIndex, Is.EqualTo(-1));
                debugPass.LocalVSMIndex = 3;
                Assert.That(debugPass.LocalVSMIndex, Is.EqualTo(3));
            }
            finally
            {
                debugPass.Dispose();
            }

            Assert.That(VSMReceiverDebugPass.IsLocalMode(VSMReceiverDebugMode.ShadowMask), Is.False);
            Assert.That(VSMReceiverDebugPass.IsLocalMode(VSMReceiverDebugMode.LocalShadowMask), Is.True);
            Assert.That(VSMReceiverDebugPass.IsLocalMode(VSMReceiverDebugMode.LocalLightCount), Is.True);
            Assert.That(VSMReceiverDebugPass.IsLocalMode(VSMReceiverDebugMode.LocalSelectedMip), Is.True);
            Assert.That(VSMReceiverDebugPass.IsLocalMode(VSMReceiverDebugMode.LocalCacheState), Is.True);
            Assert.That(VSMReceiverDebugPass.IsLocalMode(VSMReceiverDebugMode.LocalSMRTWork), Is.True);
            Assert.That(VSMReceiverDebugPass.NormalizeMode(VSMReceiverDebugMode.LocalSMRTWork),
                Is.EqualTo(VSMReceiverDebugMode.LocalSMRTWork));

            Shader shader = Shader.Find(VSMDebugPass.VSMDebugShaderName);
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                Assert.That(material.passCount, Is.GreaterThanOrEqualTo(3),
                    "VSMDebug.shader must expose Pass 2 (VSMLocalPageDebug).");
                ShaderUtil.CompilePass(material, 2, true);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
