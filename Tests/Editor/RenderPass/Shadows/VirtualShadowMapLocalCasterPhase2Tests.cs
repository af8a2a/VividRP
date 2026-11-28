using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.VirtualShadowMap;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapLocalCasterPhase2Tests
    {
        private const string ProductionShader = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";
        private const uint PageAllocatedBit = 1u << 1;
        private const uint PageDirtyBit = 1u << 2;
        private const uint PageDynamicDirtyBit = 1u << 15;
        private const uint PageRequestedBit = 1u << 0;
        private const uint PagePrimaryRequestedBit = 1u << 2;
        private const uint PageCoarseRequestedBit = 1u << 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct InvalidationSource
        {
            public Vector4 BoundsMin;
            public Vector4 BoundsMax;
            public uint4 State;
        }

        [Test]
        public void Registry_BoundRectOffsetsAndLocalCasterPipelinePrepareAllocateZeroInSteadyState()
        {
            var registry = new VirtualShadowMapRegistry();
            var clipmaps = new VirtualShadowMapClipmapLayout();
            clipmaps.Update(Vector3.zero, Quaternion.Euler(45f, 30f, 0f),
                new Bounds(Vector3.zero, Vector3.one * 50f), 100f, 512, 6, 1f, 1ul, 10ul);

            var locals = new[]
            {
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 300ul,
                    LightType = VividVSMLightType.Point,
                    PositionWS = new Vector3(0f, 3f, 0f),
                    Rotation = Quaternion.identity,
                    Range = 16f,
                    MaxResolution = 1024, // 8x8 -> 4 mips per face -> 6 * 4 = 24 bound rects
                    UseReceiverMask = true,
                    PunctualLightIndex = 0u,
                },
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 301ul,
                    LightType = VividVSMLightType.Spot,
                    PositionWS = new Vector3(5f, 3f, 2f),
                    Rotation = Quaternion.identity,
                    Range = 20f,
                    SpotAngle = 60f,
                    MaxResolution = 512, // 4x4 -> 3 mips -> 3 bound rects
                    UseReceiverMask = true,
                    PunctualLightIndex = 1u,
                },
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 302ul,
                    LightType = VividVSMLightType.Spot,
                    PositionWS = new Vector3(0f, 3f, 100f),
                    Rotation = Quaternion.identity,
                    Range = 10f,
                    SpotAngle = 45f,
                    IsSinglePage = true, // 1 mip -> 1 bound rect
                    PunctualLightIndex = 2u,
                },
            };

            registry.PrepareFrame(1ul, clipmaps, locals, locals.Length);
            int dirCount = registry.DirectionalCount;
            Assert.That(registry.TotalBoundRectCount, Is.EqualTo(dirCount + 24 + 3 + 1));
            Assert.That(registry.Projections[dirCount].Extended.y, Is.EqualTo((uint)dirCount));
            Assert.That(registry.Projections[dirCount + 6].Extended.y, Is.EqualTo((uint)(dirCount + 24)));
            Assert.That(registry.Projections[dirCount + 7].Extended.y, Is.EqualTo((uint)(dirCount + 27)));
            registry.CommitRecordedFrame();

            if (!VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform())
                return;

            using var pipeline = new VirtualShadowMapLocalCasterPipeline();
            // Warm up GPU buffers.
            for (int w = 0; w < 8; w++)
            {
                registry.PrepareFrame(1ul, clipmaps, locals, locals.Length);
                Assert.That(pipeline.PrepareForPass(null, registry, 64), Is.True);
                registry.CommitRecordedFrame();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                registry.PrepareFrame(1ul, clipmaps, locals, locals.Length);
                pipeline.PrepareForPass(null, registry, 64);
                registry.CommitRecordedFrame();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void GPU_UnifiedAllocateAndCompactViews_SyncsDescriptorsAndCompactsActiveMips()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProductionShader);
            Assert.That(source, Is.Not.Null);
            var shader = Object.Instantiate(source);

            // 1 Point light (6 faces, 512 res = 4x4 mip0 -> 3 mips, 21 entries/face -> 126 entries, 18 bound rects).
            var registry = new VirtualShadowMapRegistry();
            var locals = new[]
            {
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 400ul,
                    LightType = VividVSMLightType.Point,
                    PositionWS = Vector3.zero,
                    Rotation = Quaternion.identity,
                    Range = 16f,
                    MaxResolution = 512,
                    UseReceiverMask = true,
                    PunctualLightIndex = 0u,
                },
            };
            registry.PrepareFrame(1ul, null, locals, 1);
            int projCount = registry.ProjectionCount;
            int totalEntries = registry.TotalPageTableEntries;
            int totalHierarchy = Mathf.Max(registry.TotalHierarchyNodes, 1);
            int totalBoundRects = registry.TotalBoundRectCount;
            Assert.That(projCount, Is.EqualTo(6));
            Assert.That(totalEntries, Is.EqualTo(6 * 21));
            Assert.That(totalBoundRects, Is.EqualTo(18));

            const int capacity = 16;
            const int pagesPerRow = 4;
            int listWords = capacity * 4 + 4;

            using var projBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, projCount, VirtualShadowMapProjection.Stride);
            using var reqFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint));
            using var reqMasks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint) * 2);
            using var pageTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint));
            using var pageMeta = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint) * 4);
            using var physOwners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            using var physDescs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint) * 2);
            using var physLists = new GraphicsBuffer(GraphicsBuffer.Target.Structured, listWords, sizeof(uint));
            using var physMasks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint) * 2);
            using var allocCounters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, sizeof(uint));
            using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint) * 4);
            using var localAllocCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var hierarchy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalHierarchy, sizeof(uint) * 3);
            using var boundRects = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalBoundRects * 2, sizeof(uint) * 4);
            using var activeViews = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 32, VividVSMActiveView.Stride);
            using var activeViewCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var instDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 3, sizeof(uint));
            using var jobDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 4, sizeof(uint));
            using var pageIndirectArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 32, sizeof(uint));
            try
            {
                projBuf.SetData(registry.Projections, 0, 0, projCount);
                pageTable.SetData(new uint[totalEntries]);
                pageMeta.SetData(new uint4[totalEntries]);
                physOwners.SetData(new uint[capacity]);
                physDescs.SetData(new uint2[capacity]);
                physMasks.SetData(new uint2[capacity]);
                pressure.SetData(new uint4[3]);
                localAllocCount.SetData(new uint[] { 0u });

                var initLists = new uint[listWords];
                for (int s = 0; s < capacity; s++) initLists[s] = (uint)s;
                physLists.SetData(initLists);

                // Request only 2 pages: Face 0 mip 0 page (2, 1), and Face 4 mip 1 page (1, 0).
                var requests = new uint[totalEntries];
                var masks = new uint2[totalEntries];
                uint pFace0Mip0 = VirtualShadowMapAddressing.CalculatePageIndex(
                    registry.Projections[0].Addressing.x, 4u, false, 0u, new uint2(2u, 1u));
                uint pFace4Mip1 = VirtualShadowMapAddressing.CalculatePageIndex(
                    registry.Projections[4].Addressing.x, 4u, false, 1u, new uint2(1u, 0u));
                requests[pFace0Mip0] = PageRequestedBit | PagePrimaryRequestedBit;
                requests[pFace4Mip1] = PageRequestedBit | PageCoarseRequestedBit;
                masks[pFace0Mip0] = new uint2(0xFFFFFFFFu, 0xFFFFFFFFu);
                masks[pFace4Mip1] = new uint2(0xFFFFFFFFu, 0xFFFFFFFFu);
                reqFlags.SetData(requests);
                reqMasks.SetData(masks);

                shader.SetInt("_VSMPrototypePageSize", VirtualShadowMapPrototypeRuntime.PageSize);
                shader.SetInt("_VSMPrototypePhysicalPagesPerRow", pagesPerRow);
                shader.SetInt("_VSMPrototypePageTableEntryCount", totalEntries);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", capacity);
                shader.SetInt("_VSMPrototypeFeedbackFrameIndex", 1);
                shader.SetInt("_VSMReceiverMaskEnabled", 1);
                shader.SetInt("_VSMProjectionCount", projCount);
                shader.SetInt("_VSMUnifiedMaxEntriesPerVSM", 21);
                shader.SetInt("_VSMAllocateStartVSMId", 0);
                shader.SetInt("_VSMAllocatePassMode", 0);
                shader.SetInt("_VSMLocalMaxPhysicalPages", 0);

                int kReset = shader.FindKernel("VSMResetPhysicalPageListsUE");
                int kUpdate = shader.FindKernel("VSMUpdatePhysicalPagesUnified");
                int kPack = shader.FindKernel("VSMPackAvailablePagesUE");
                int kAppendEmpty = shader.FindKernel("VSMAppendEmptyPhysicalPagesUE");
                int kAlloc = shader.FindKernel("VSMAllocateNewPageMappingsUnified");
                int kClearBounds = shader.FindKernel("VSMClearUnifiedPageBounds");
                int kBuildHier = shader.FindKernel("VSMBuildUnifiedPageCullHierarchy");
                int kCompact = shader.FindKernel("VSMCompactUnifiedViews");

                shader.SetBuffer(kReset, "_VSMPhysicalPageLists", physLists);
                shader.SetBuffer(kReset, "_VSMPrototypeAllocatorCounters", allocCounters);
                shader.SetBuffer(kReset, "_VSMPagePressureRW", pressure);
                shader.Dispatch(kReset, 1, 1, 1);

                shader.SetBuffer(kUpdate, "_VSMProjections", projBuf);
                shader.SetBuffer(kUpdate, "_VSMPhysicalPageLists", physLists);
                shader.SetBuffer(kUpdate, "_VSMPrototypePhysicalPageOwners", physOwners);
                shader.SetBuffer(kUpdate, "_VSMPhysicalPageDescriptors", physDescs);
                shader.SetBuffer(kUpdate, "_VSMPrototypePageMetadata", pageMeta);
                shader.SetBuffer(kUpdate, "_VSMPrototypeWritablePageTable", pageTable);
                shader.SetBuffer(kUpdate, "_VSMPageRequestFlags", reqFlags);
                shader.SetBuffer(kUpdate, "_VSMPrototypeAllocatorCounters", allocCounters);
                shader.SetBuffer(kUpdate, "_VSMLocalAllocatedCountRW", localAllocCount);
                shader.Dispatch(kUpdate, 1, 1, 1);

                shader.SetBuffer(kPack, "_VSMPhysicalPageLists", physLists);
                shader.Dispatch(kPack, 1, 1, 1);

                shader.SetBuffer(kAppendEmpty, "_VSMPhysicalPageLists", physLists);
                shader.Dispatch(kAppendEmpty, 1, 1, 1);

                shader.SetBuffer(kAlloc, "_VSMProjections", projBuf);
                shader.SetBuffer(kAlloc, "_VSMPageRequestFlags", reqFlags);
                shader.SetBuffer(kAlloc, "_VSMPrototypePageMetadata", pageMeta);
                shader.SetBuffer(kAlloc, "_VSMPrototypeWritablePageTable", pageTable);
                shader.SetBuffer(kAlloc, "_VSMPrototypePhysicalPageOwners", physOwners);
                shader.SetBuffer(kAlloc, "_VSMPhysicalPageDescriptors", physDescs);
                shader.SetBuffer(kAlloc, "_VSMPhysicalReceiverMasks", physMasks);
                shader.SetBuffer(kAlloc, "_VSMPhysicalPageLists", physLists);
                shader.SetBuffer(kAlloc, "_VSMPrototypeAllocatorCounters", allocCounters);
                shader.SetBuffer(kAlloc, "_VSMPagePressureRW", pressure);
                shader.SetBuffer(kAlloc, "_VSMLocalAllocatedCountRW", localAllocCount);
                shader.Dispatch(kAlloc, 1, projCount, 1);

                // Verify authoritative descriptors written for both allocated slots.
                var descs = new uint2[capacity];
                var owners = new uint[capacity];
                physDescs.GetData(descs);
                physOwners.GetData(owners);
                int validDescs = 0;
                bool foundFace0 = false, foundFace4 = false;
                for (int s = 0; s < capacity; s++)
                {
                    if (owners[s] == 0u) continue;
                    VirtualShadowMapAddressing.DecodePhysicalOwnerDesc(descs[s], out uint vsmId, out uint mip, out uint2 xy, out VividVSMOwnerFlags flags);
                    Assert.That(flags & VividVSMOwnerFlags.Valid, Is.Not.Zero);
                    validDescs++;
                    if (vsmId == 0u && mip == 0u && xy.Equals(new uint2(2u, 1u))) foundFace0 = true;
                    if (vsmId == 4u && mip == 1u && xy.Equals(new uint2(1u, 0u))) foundFace4 = true;
                }
                Assert.That(validDescs, Is.EqualTo(2));
                Assert.That(foundFace0, Is.True);
                Assert.That(foundFace4, Is.True);

                // Build unified bounds/hierarchy and compact views for Static layer (0).
                shader.SetInt("_VSMUnifiedTotalHierarchyNodes", totalHierarchy);
                shader.SetInt("_VSMUnifiedTotalBoundRects", totalBoundRects);
                shader.SetBuffer(kClearBounds, "_VSMPageCullHierarchyRW", hierarchy);
                shader.SetBuffer(kClearBounds, "_VSMUncachedPageRectBoundsRW", boundRects);
                shader.Dispatch(kClearBounds, 1, 1, 1);

                shader.SetBuffer(kBuildHier, "_VSMProjections", projBuf);
                shader.SetBuffer(kBuildHier, "_VSMPrototypePhysicalPageOwners", physOwners);
                shader.SetBuffer(kBuildHier, "_VSMPhysicalPageDescriptors", physDescs);
                shader.SetBuffer(kBuildHier, "_VSMPrototypePageMetadata", pageMeta);
                shader.SetBuffer(kBuildHier, "_VSMPrototypePageTable", pageTable);
                shader.SetBuffer(kBuildHier, "_VSMPageReceiverMasks", reqMasks);
                shader.SetBuffer(kBuildHier, "_VSMPageCullHierarchyRW", hierarchy);
                shader.SetBuffer(kBuildHier, "_VSMUncachedPageRectBoundsRW", boundRects);
                shader.Dispatch(kBuildHier, 1, 1, 1);

                shader.SetInt("_VSMPrototypeCasterLayer", 0);
                shader.SetInt("_VSMCompactStartVSMId", 0);
                shader.SetInt("_VSMUnifiedMaxActiveViews", 32);
                shader.SetInt("_VSMLocalSourceInstanceCount", 128);
                shader.SetInt("_VSMLocalMaxPageRequestsPerList", 64);
                shader.SetBuffer(kCompact, "_VSMProjections", projBuf);
                shader.SetBuffer(kCompact, "_VSMUncachedPageRectBounds", boundRects);
                shader.SetBuffer(kCompact, "_VSMUnifiedActiveViewsRW", activeViews);
                shader.SetBuffer(kCompact, "_VSMUnifiedActiveViewCountRW", activeViewCount);
                shader.SetBuffer(kCompact, "_VSMUnifiedInstanceDispatchArgsRW", instDispatchArgs);
                shader.SetBuffer(kCompact, "_VSMLocalJobDispatchArgsRW", jobDispatchArgs);
                shader.SetBuffer(kCompact, "_VSMLocalMeshletPageIndirectArgsRW", pageIndirectArgs);
                shader.Dispatch(kCompact, 1, 1, 1);

                var countOut = new uint[1];
                var viewsOut = new VividVSMActiveView[32];
                activeViewCount.GetData(countOut);
                activeViews.GetData(viewsOut);
                // Out of 6*3 = 18 possible (Face, Mip) views, ONLY 2 active views must be emitted!
                Assert.That(countOut[0], Is.EqualTo(2u));
                Assert.That(viewsOut[0].VSMId, Is.EqualTo(0u));
                Assert.That(viewsOut[0].MipLevel, Is.EqualTo(0u));
                Assert.That(viewsOut[0].VirtualResolution, Is.EqualTo(512u));
                Assert.That(viewsOut[1].VSMId, Is.EqualTo(4u));
                Assert.That(viewsOut[1].MipLevel, Is.EqualTo(1u));
                Assert.That(viewsOut[1].VirtualResolution, Is.EqualTo(256u));
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [Test]
        public void GPU_LocalCacheInvalidation_InvalidatesOnlyAffectedPointFaceAndSkipsOppositeFace()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProductionShader);
            Assert.That(source, Is.Not.Null);
            var shader = Object.Instantiate(source);

            // 1 Point light at origin, range 12, 512 res (4x4 mip0 -> 3 mips, 21 entries/face -> 126 total).
            var registry = new VirtualShadowMapRegistry();
            var locals = new[]
            {
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 410ul,
                    LightType = VividVSMLightType.Point,
                    PositionWS = Vector3.zero,
                    Rotation = Quaternion.identity,
                    Range = 12f,
                    MaxResolution = 512,
                    UseReceiverMask = true,
                    PunctualLightIndex = 0u,
                },
            };
            registry.PrepareFrame(1ul, null, locals, 1);
            int projCount = registry.ProjectionCount;
            int totalEntries = registry.TotalPageTableEntries;

            using var projBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, projCount, VirtualShadowMapProjection.Stride);
            using var pageMeta = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint) * 4);
            using var queue = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, Marshal.SizeOf<InvalidationSource>());
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 8, sizeof(uint));
            using var prevTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint));
            var dummyHzb = new RenderTexture(4, 4, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat)
            {
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                volumeDepth = 2,
            };
            dummyHzb.Create();
            try
            {
                projBuf.SetData(registry.Projections, 0, 0, projCount);
                // Mark every page as allocated & clean (no dirty bits).
                var metaInit = new uint4[totalEntries];
                for (int i = 0; i < totalEntries; i++) metaInit[i] = new uint4(PageAllocatedBit, 1u, 1u, 0u);
                pageMeta.SetData(metaInit);
                prevTable.SetData(new uint[totalEntries]);

                // Moving object strictly on +X axis at (5, 0, 0) with radius 0.5 -> must ONLY hit Face 0 (+X), NEVER Face 1 (-X)!
                queue.SetData(new[]
                {
                    new InvalidationSource
                    {
                        BoundsMin = new Vector4(4.5f, -0.5f, -0.5f, 0f),
                        BoundsMax = new Vector4(5.5f, 0.5f, 0.5f, 0f),
                        State = new uint4(PageDynamicDirtyBit, 1u, 1u, 1u),
                    },
                });
                // Args layout: [0..2]=dispatch, [3]=queued count (1), [4]=atomic job cursor (0), [5..7]=stats
                args.SetData(new uint[] { 6u, 1u, 1u, 1u, 0u, 0u, 0u, 0u });

                int kernel = shader.FindKernel("VSMProcessInvalidationQueue");
                shader.SetInt("_VSMProjectionCount", projCount);
                shader.SetInt("_VSMPrototypePagesPerAxis", 4);
                shader.SetInt("_VSMPrototypePageSize", VirtualShadowMapPrototypeRuntime.PageSize);
                shader.SetInt("_VSMPrototypeVirtualResolution", 512);
                shader.SetInt("_VSMPrototypePageTableEntryCount", totalEntries);
                shader.SetInt("_VSMPrototypePhysicalPagesPerRow", 4);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 16);
                shader.SetInt("_VSMInvalidateUseHZB", 0);
                shader.SetInt("_VSMHZBHistoryValid", 0);
                shader.SetBuffer(kernel, "_VSMProjections", projBuf);
                shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", pageMeta);
                shader.SetBuffer(kernel, "_VSMInvalidationQueue", queue);
                shader.SetBuffer(kernel, "_VSMInvalidationArgs", args);
                shader.SetBuffer(kernel, "_VSMHZBPreviousTable", prevTable);
                shader.SetTexture(kernel, "_VSMHZB", dummyHzb);
                shader.Dispatch(kernel, 6, 1, 1);

                var metaOut = new uint4[totalEntries];
                pageMeta.GetData(metaOut);

                bool face0Dirty = false;
                bool face1Dirty = false;
                for (int i = 0; i < 21; i++)
                {
                    if ((metaOut[i].x & PageDynamicDirtyBit) != 0u) face0Dirty = true;
                    if ((metaOut[21 + i].x & PageDynamicDirtyBit) != 0u) face1Dirty = true;
                }
                Assert.That(face0Dirty, Is.True);  // +X face invalidated
                Assert.That(face1Dirty, Is.False); // -X face remains clean
            }
            finally
            {
                dummyHzb.Release();
                Object.DestroyImmediate(dummyHzb);
                Object.DestroyImmediate(shader);
            }
        }
    }
}
