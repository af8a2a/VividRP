using VividRP.Runtime.VirtualShadowMap;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapClipmapTests
    {
        private static void Update(VirtualShadowMapClipmapLayout layout, Vector3 position,
            ulong camera = 1, ulong light = 2, Quaternion? rotation = null, Bounds? bounds = null)
            => layout.Update(position, rotation ?? Quaternion.identity,
                bounds ?? new Bounds(Vector3.zero, Vector3.one * 100), 150, 512, 6, 1, camera, light);

        [Test]
        public void UELayout_UsesSeventeenLevelsAndQuarterWindowSnaps()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            Update(layout, Vector3.zero);
            Assert.That(layout.Count, Is.EqualTo(17));
            Assert.That(layout.Radii[0], Is.EqualTo(2.56f));
            Assert.That(layout.Radii[16], Is.EqualTo(167772.16f));
            Matrix4x4 view = layout.Views[0];
            Update(layout, new Vector3(.63f, -.63f, 0));
            Assert.That(layout.Views[0], Is.EqualTo(view));
            Update(layout, new Vector3(.65f, -.65f, 0));
            Assert.That(layout.OriginX[0], Is.EqualTo(-1));
            Assert.That(layout.OriginY[0], Is.EqualTo(-3));
            Assert.That(layout.Views[0].m03, Is.EqualTo(-1.28f));
            Assert.That(layout.Views[0].m13, Is.EqualTo(1.28f));
            Assert.That(VirtualShadowMapClipmapLayout.SnapCenter(.5, 1), Is.EqualTo(1));
            Assert.That(VirtualShadowMapClipmapLayout.SnapCenter(-.5, 1), Is.EqualTo(0));
        }

        [Test]
        public void UELayout_RetainedPageHasIdenticalTexelAndDepthAfterScroll()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            Quaternion rotation = Quaternion.Euler(35, 23, 0);
            Update(layout, Vector3.zero, rotation: rotation);
            Matrix4x4 before = VividShadowData.BuildWorldToShadowMatrix(layout.Projections[0], layout.Views[0]);
            long x = layout.OriginX[0], y = layout.OriginY[0];
            Update(layout, rotation * new Vector3(.7f, -.7f, 0), rotation: rotation);
            Matrix4x4 after = VividShadowData.BuildWorldToShadowMatrix(layout.Projections[0], layout.Views[0]);
            Vector3 point = rotation * new Vector3(1.25f, -1.25f, 8);
            Vector3 a = before.MultiplyPoint3x4(point), b = after.MultiplyPoint3x4(point);
            Assert.That((a.x - b.x) * 512, Is.EqualTo((layout.OriginX[0] - x) * 128).Within(.001));
            Assert.That((a.y - b.y) * 512, Is.EqualTo((layout.OriginY[0] - y) * 128).Within(.001));
            Assert.That(b.z, Is.EqualTo(a.z).Within(1e-7));
        }

        [Test]
        public void UELayout_DepthGuardInvalidatesOnlyEscapedLevel()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            using var projections = new VirtualShadowMapProjectionSet();
            Update(layout, Vector3.zero);
            projections.PrepareClipmaps(layout); projections.CommitRecordedLayout();
            ulong generation = projections.Generation;
            float coarseMin = layout.DepthMins[1];
            Update(layout, new Vector3(0, 0, 1150), bounds: new Bounds(Vector3.one * 100000, Vector3.one));
            Assert.That(layout.DepthMins[0], Is.EqualTo(-1280));
            Update(layout, new Vector3(0, 0, 1152));
            Assert.That(layout.DepthMins[0], Is.EqualTo(-128));
            Assert.That(layout.DepthMins[1], Is.EqualTo(coarseMin));
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.True);
            Assert.That(projections.Generation, Is.EqualTo(generation));
            using var cmd = new CommandBuffer(); projections.Upload(cmd); Graphics.ExecuteCommandBuffer(cmd);
            var remap = new int4[layout.Count]; projections.RemapBuffer.GetData(remap);
            Assert.That(remap[0].z, Is.EqualTo(1));
            for (int i = 1; i < remap.Length; i++) Assert.That(remap[i].z, Is.Zero);
            projections.Reset(); projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.True);
            projections.CommitRecordedLayout(); projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.False);
        }

        [Test]
        public void UELayout_CameraRotationAndBoundsDoNotMoveWindows()
        {
            var go = new GameObject("VSM UE layout");
            try
            {
                var camera = go.AddComponent<Camera>();
                var view = new VividCameraData { camera = camera };
                var layout = new VirtualShadowMapClipmapLayout();
                using var projections = new VirtualShadowMapProjectionSet();
                var bounds = new Bounds(Vector3.zero, Vector3.one * 100);
                layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 4096, 6, 1, 1, 2, coverageView: view);
                projections.PrepareClipmaps(layout); projections.CommitRecordedLayout();
                ulong generation = projections.Generation;
                for (int i = 0; i < 160; i++)
                {
                    camera.transform.rotation = Quaternion.Euler(i, i * 7, 0);
                    camera.fieldOfView = 30 + i % 90;
                    layout.Reset();
                    layout.Update(Vector3.zero, Quaternion.identity, new Bounds(Vector3.one * i, Vector3.one),
                        10 + i, 4096, 6, 1, 1, 2, coverageView: view);
                    projections.PrepareClipmaps(layout);
                    Assert.That(projections.RequiresRemap, Is.False);
                    Assert.That(projections.Generation, Is.EqualTo(generation));
                }
                camera.orthographic = true; camera.orthographicSize = 10; camera.aspect = 2;
                camera.ResetProjectionMatrix();
                layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 4096, 6, 1, 1, 2, coverageView: view);
                Assert.That(layout.FirstLevel, Is.EqualTo(10)); // floor(log2(20 m * 100))
                Assert.That(layout.Count, Is.EqualTo(13));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void UELayout_StablePreparationAllocatesZero()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            using var projections = new VirtualShadowMapProjectionSet();
            for (int i = 0; i < 32; i++)
            { Update(layout, Vector3.zero); projections.PrepareClipmaps(layout); projections.CommitRecordedLayout(); }
            var buffer = projections.Buffer;
            ulong generation = projections.Generation;
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            { layout.Reset(); Update(layout, Vector3.zero); projections.PrepareClipmaps(layout); projections.CommitRecordedLayout(); }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(projections.Buffer, Is.SameAs(buffer));
            Assert.That(projections.Generation, Is.EqualTo(generation));
            Assert.That(projections.RequiresRemap, Is.False);
        }

        [Test]
        public void ProjectionHistory_AdvancesOnlyWhenRecordedAndResetsOnBasisChanges()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            using var projections = new VirtualShadowMapProjectionSet();
            Update(layout, Vector3.zero);
            projections.PrepareClipmaps(layout); projections.CommitRecordedLayout();
            ulong generation = projections.Generation;
            Update(layout, new Vector3(.7f, 0, 0)); projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.True);
            Assert.That(projections.RequiresFeedbackReset, Is.False);
            Assert.That(projections.Generation, Is.EqualTo(generation));
            projections.Reset(); projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.True);
            projections.CommitRecordedLayout(); projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.False);
            Update(layout, Vector3.zero, camera: 3); projections.PrepareClipmaps(layout);
            Assert.That(projections.Generation, Is.EqualTo(generation + 1));
            Assert.That(projections.RequiresFeedbackReset, Is.True);
        }

        [Test]
        public void CacheKey_ClipmapGenerationAllowsMoreThanFourLevelsAndTracksNonProjectionInputs()
        {
            var key = Key();
            Assert.That(key.IsValid, Is.True);
            Assert.That(key.Equals(Key()), Is.True);
            Assert.That(key.Equals(Key(generation: 2)), Is.False);
            Assert.That(key.Equals(Key(mask: 1)), Is.False);
            Assert.That(key.Equals(Key(lod: 2)), Is.False);
            Assert.That(key.Equals(Key(error: 2)), Is.False);
        }

        [TestCase(256)]
        [TestCase(256, true)]
        [TestCase(1024, true)]
        [TestCase(384)]
        [TestCase(512)]
        [TestCase(768)]
        [TestCase(1024)]
        public void PageBudget_ResizesAllPhysicalResourcesAndReusesTheStableConfiguration(int budget, bool windows = false)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            bool previousWindows = VirtualShadowMapPrototypeRuntime.ExperimentalPageWindows;
            try
            {
                VirtualShadowMapPrototypeRuntime.ExperimentalPageWindows = windows;
                VirtualShadowMapPrototypeRuntime.EnsureResources(4096, 10, 256);
                Assert.That(VirtualShadowMapPrototypeRuntime.EnsureResources(4096, 10, budget), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity, Is.EqualTo(budget));
                Assert.That(VirtualShadowMapPrototypeRuntime.RasterDepth.rt.volumeDepth, Is.EqualTo(windows ? 1 : budget));
                Assert.That(VirtualShadowMapPrototypeRuntime.PhysicalPageOwners.count, Is.EqualTo(budget));
                Assert.That(VirtualShadowMapPrototypeRuntime.PhysicalPagePool.rt.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
                Assert.That(VirtualShadowMapPrototypeRuntime.PhysicalPagePool.rt.volumeDepth, Is.EqualTo(VirtualShadowMapPrototypeRuntime.PhysicalPoolArraySize));
                Assert.That(VirtualShadowMapPrototypeRuntime.RasterDepth.rt.width,
                    Is.EqualTo(VirtualShadowMapPrototypeRuntime.PageSize * (windows ? 4 : 1)));
                var raster = VirtualShadowMapPrototypeRuntime.RasterDepth;
                var pool = VirtualShadowMapPrototypeRuntime.PhysicalPagePool;
                var workList = VirtualShadowMapPrototypeRuntime.PageWorkList;
                var workArgs = VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgs;
                var mergeList = VirtualShadowMapPrototypeRuntime.MergePageWorkList;
                var mergeArgs = VirtualShadowMapPrototypeRuntime.MergePageDispatchArgs;
                Assert.That(mergeList.count, Is.EqualTo(budget));
                Assert.That(mergeArgs.count, Is.EqualTo(3));
                Assert.That(mergeArgs.target, Is.EqualTo(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments));
                var remapMetadata = VirtualShadowMapPrototypeRuntime.RemapPageMetadata;
                var requestFlags = VirtualShadowMapPrototypeRuntime.PageRequestFlags;
                Assert.That(requestFlags.count, Is.EqualTo(VirtualShadowMapPrototypeRuntime.PageTableEntryCount));
                Assert.That(requestFlags.stride, Is.EqualTo(4));
                Assert.That(remapMetadata.count, Is.EqualTo(budget));
                Assert.That(remapMetadata.stride, Is.EqualTo(16));
                Assert.That(workList.count, Is.EqualTo(budget * 2));
                Assert.That(workArgs.count, Is.EqualTo(6));
                Assert.That(workArgs.target, Is.EqualTo(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments));
                for (int i = 0; i < 32; i++) VirtualShadowMapPrototypeRuntime.EnsureResources(4096, 10, budget);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++) VirtualShadowMapPrototypeRuntime.EnsureResources(4096, 10, budget);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
                Assert.That(VirtualShadowMapPrototypeRuntime.RasterDepth, Is.SameAs(raster));
                Assert.That(VirtualShadowMapPrototypeRuntime.PhysicalPagePool, Is.SameAs(pool));
                Assert.That(VirtualShadowMapPrototypeRuntime.PageWorkList, Is.SameAs(workList));
                Assert.That(VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgs, Is.SameAs(workArgs));
                Assert.That(VirtualShadowMapPrototypeRuntime.MergePageWorkList, Is.SameAs(mergeList));
                Assert.That(VirtualShadowMapPrototypeRuntime.MergePageDispatchArgs, Is.SameAs(mergeArgs));
                Assert.That(VirtualShadowMapPrototypeRuntime.RemapPageMetadata, Is.SameAs(remapMetadata));
                Assert.That(VirtualShadowMapPrototypeRuntime.PageRequestFlags, Is.SameAs(requestFlags));
                VirtualShadowMapPrototypeRuntime.EnsureResources(4096, 10, 256);
                Assert.That(VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity, Is.EqualTo(256));
                Assert.That(VirtualShadowMapPrototypeRuntime.PageWorkList.count, Is.EqualTo(512));
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.ExperimentalPageWindows = previousWindows;
                VirtualShadowMapPrototypeRuntime.ReleaseResources();
            }
            Assert.That(VirtualShadowMapPrototypeRuntime.RemapPageMetadata, Is.Null);
            Assert.That(VirtualShadowMapPrototypeRuntime.PageRequestFlags, Is.Null);
            Assert.That(VirtualShadowMapPrototypeRuntime.PageWorkList, Is.Null);
            Assert.That(VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgs, Is.Null);
        }

        private static VirtualShadowMapPrototypeCacheKey Key(ulong generation = 1, int mask = -1,
            int lod = -1, float error = 1)
            => new(1, 1, 1, 1, 8, 512, lod, error, 1, new Vector4(0, 0, 1, 0), mask, generation);

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(-1, 1)]
        [TestCase(4, 0)]
        [TestCase(-4, -4)]
        [TestCase(0, 0, true)]
        [TestCase(1, -1, false, true)]
        public void Remap_PreservesFeedbackAndSlotsAndAllocatorReusesHoles(int dx, int dy,
            bool resetBasis = false, bool mixedLevels = false)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(source, Is.Not.Null);
            ComputeShader shader = Object.Instantiate(source);
            using var receiverMasks = new VirtualShadowMapReceiverMaskTestBuffers(shader);
            try
            {
                // Seventeen levels exercise the UE default count beyond the old 16-level limit.
                const int count = 272, capacity = 65;
                var tableData = new uint[count];
                var metadataData = new uint4[count];
                tableData[0] = 1;
                tableData[10] = 4;
                tableData[261] = 65; // Exercise the second physical dispatch group.
                for (int i = 0; i < count; i++)
                    metadataData[i] = new uint4(tableData[i] == 0 ? 0u : 10u, tableData[i], 7, 8);
                // Keep dirty, deferred, occupancy, age and debug state bit-for-bit.
                metadataData[0].x |= (1u << 2) | (1u << 17);
                metadataData[10].x |= (1u << 15) | (1u << 14);
                metadataData[261].w = 0x12345678u;
                // Unallocated feedback must not reappear as cached state after remap.
                metadataData[54] = new uint4(0u, 0u, 6u, 0xfeedu);
                using var remapMetadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 16);
                using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
                using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
                using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
                using var counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
                using var remap = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 17, 16);
                using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (count + 31) / 32, 4);
                using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 16);
                pressure.SetData(new uint4[3]);
                table.SetData(tableData);
                metadata.SetData(metadataData);
                var ownersData = new uint[capacity];
                for (int page = 0; page < count; page++)
                    if (tableData[page] != 0u) ownersData[tableData[page] - 1u] = (uint)page + 1u;
                owners.SetData(ownersData);
                var remaps = new int4[17];
                for (int i = 0; i < 17; i++) remaps[i] = new int4(dx, dy, resetBasis ? 1 : 0, 0);
                remap.SetData(remaps);
                if (mixedLevels)
                {
                    remaps[0] = int4.zero;
                    remaps[16] = new int4(-1, 0, 0, 0);
                    remap.SetData(remaps);
                }
                int update = shader.FindKernel("VSMUpdatePhysicalPageAddresses");
                int clear = shader.FindKernel("VSMClearVirtualPageMappings");
                int move = shader.FindKernel("VSMRemapPages");
                int allocate = shader.FindKernel("VSMPrototypeAllocatePages");
                shader.SetInt("_VSMProjectionCount", 17);
                shader.SetInt("_VSMPrototypePageTableEntryCount", count);
                shader.SetInt("_VSMPrototypePagesPerAxis", 4);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", capacity);
                shader.SetInt("_VSMPrototypeFeedbackFrameIndex", 7);
                shader.SetBuffer(update, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(update, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(update, "_VSMRemapPageMetadata", remapMetadata);
                shader.SetBuffer(update, "_VSMProjectionRemap", remap);
                shader.Dispatch(update, (capacity + 63) / 64, 1, 1);
                shader.SetBuffer(clear, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(clear, "_VSMPrototypePageMetadata", metadata);
                shader.Dispatch(clear, (count + 63) / 64, 1, 1);
                shader.SetBuffer(move, "_VSMRemapPageMetadata", remapMetadata);
                shader.SetBuffer(move, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(move, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(move, "_VSMPrototypePhysicalPageOwners", owners);
                shader.Dispatch(move, (capacity + 63) / 64, 1, 1);
                var actual = new uint[count];
                var actualMetadata = new uint4[count];
                var actualOwners = new uint[capacity];
                table.GetData(actual);
                metadata.GetData(actualMetadata);
                owners.GetData(actualOwners);
                var expectedOwners = new uint[capacity];
                for (int dest = 0; dest < count; dest++)
                {
                    int4 delta = remaps[dest / 16];
                    int x = dest % 4 + delta.x, y = dest % 16 / 4 + delta.y;
                    bool inside = delta.z == 0 && x >= 0 && x < 4 && y >= 0 && y < 4;
                    int src = dest / 16 * 16 + y * 4 + x;
                    Assert.That(actual[dest], Is.EqualTo(inside ? tableData[src] : 0u));
                    Assert.That(actualMetadata[dest], Is.EqualTo(inside && tableData[src] != 0u ? metadataData[src] : uint4.zero));
                    if (actual[dest] != 0)
                        expectedOwners[actual[dest] - 1] = (uint)dest + 1;
                }
                Assert.That(actualOwners, Is.EqualTo(expectedOwners));
                int free = System.Array.IndexOf(actualOwners, 0u);
                int missing = System.Array.IndexOf(actual, 0u);
                Assert.That(free, Is.GreaterThanOrEqualTo(0));
                using var requestFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
                var demand = new uint[count]; demand[missing] = 1u;
                requestFlags.SetData(demand);
                shader.SetBuffer(allocate, "_VSMPageRequestFlags", requestFlags);
                shader.SetBuffer(allocate, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(allocate, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(allocate, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(allocate, "_VSMPrototypeAllocatorCounters", counters);
                int prepare = shader.FindKernel("VSMPrototypePrepareAllocation");
                shader.SetBuffer(prepare, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(prepare, "_VSMPageRequestFlags", requestFlags);
                shader.SetBuffer(prepare, "_VSMAllocationRequests", requests);
                shader.Dispatch(prepare, (count + 63) / 64, 1, 1);
                shader.SetBuffer(allocate, "_VSMAllocationRequests", requests);
                shader.SetBuffer(allocate, "_VSMPagePressureRW", pressure);
                shader.Dispatch(allocate, 1, 1, 1);
                var allocated = new uint[count];
                table.GetData(allocated);
                Assert.That(allocated[missing], Is.EqualTo((uint)free + 1));
                for (int i = 0; i < count; i++)
                    if (actual[i] != 0) Assert.That(allocated[i], Is.EqualTo(actual[i]));
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
