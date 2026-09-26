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
        {
            layout.Update(position, rotation ?? Quaternion.identity,
                bounds ?? new Bounds(Vector3.zero, Vector3.one * 100), 150, 512, 2, 1, camera, light);
        }

        [Test]
        public void Layout_UsesPowerOfTwoLevelsAndStablePageAlignedXY()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            Update(layout, new Vector3(0.1f, 0.1f, 0));
            Assert.That(layout.Count, Is.EqualTo(8));
            Assert.That(layout.Radii[0], Is.EqualTo(4));
            Assert.That(layout.Radii[7], Is.EqualTo(512));
            Matrix4x4 view = layout.Views[0];
            Update(layout, new Vector3(1.9f, 1.9f, 0));
            Assert.That(layout.Views[0], Is.EqualTo(view));
            Update(layout, new Vector3(2.1f, -0.1f, 0));
            Assert.That(layout.OriginX[0], Is.EqualTo(-1));
            Assert.That(layout.OriginY[0], Is.EqualTo(-3));
            Assert.That(layout.Views[0].m03, Is.EqualTo(view.m03 - 2));
            Assert.That(layout.Views[0].m13, Is.EqualTo(view.m13 + 2));
        }

        [Test]
        public void Layout_RetainedPageHasIdenticalLocalTexelAndDepthAfterScroll()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            Quaternion rotation = Quaternion.Euler(35, 23, 0);
            Update(layout, rotation * new Vector3(0.1f, 0.1f, 0), rotation: rotation);
            Matrix4x4 before = VividShadowData.BuildWorldToShadowMatrix(layout.Projections[0], layout.Views[0]);
            long x = layout.OriginX[0], y = layout.OriginY[0];
            Update(layout, rotation * new Vector3(2.1f, -0.1f, 0), rotation: rotation);
            Matrix4x4 after = VividShadowData.BuildWorldToShadowMatrix(layout.Projections[0], layout.Views[0]);
            Vector3 point = rotation * new Vector3(1.25f, -1.25f, 8);
            Vector3 a = before.MultiplyPoint3x4(point), b = after.MultiplyPoint3x4(point);
            Assert.That((a.x - b.x) * 512, Is.EqualTo((layout.OriginX[0] - x) * 128).Within(0.001));
            Assert.That((a.y - b.y) * 512, Is.EqualTo((layout.OriginY[0] - y) * 128).Within(0.001));
            Assert.That(b.z, Is.EqualTo(a.z).Within(1e-7));
        }

        [Test]
        public void Layout_DepthIntervalSurvivesShrinkButExpandsOnEscape()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            Update(layout, Vector3.zero);
            float min = layout.DepthMin, max = layout.DepthMax;
            Update(layout, new Vector3(10, -10, 30), bounds: new Bounds(Vector3.zero, Vector3.one));
            Assert.That(layout.DepthMin, Is.EqualTo(min));
            Assert.That(layout.DepthMax, Is.EqualTo(max));
            Update(layout, new Vector3(0, 0, 2000));
            Assert.That(layout.DepthMax, Is.GreaterThanOrEqualTo(2150));
            Assert.That(layout.DepthMin, Is.LessThanOrEqualTo(-50));
            Assert.That(layout.DepthMax, Is.Not.EqualTo(max));
        }

        [Test]
        public void ProjectionHistory_AdvancesOnlyWhenRecordedAndResetsOnBasisChanges()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            using var projections = new VirtualShadowMapProjectionSet();
            Update(layout, Vector3.one * 0.1f);
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresFeedbackReset, Is.True);
            projections.CommitRecordedLayout();
            ulong generation = projections.Generation;
            Update(layout, new Vector3(2.1f, 0.1f, 0));
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.True);
            Assert.That(projections.RequiresFeedbackReset, Is.False);
            Assert.That(projections.Generation, Is.EqualTo(generation));
            // Skipping Record must not hide the same pending scroll next frame.
            projections.Reset();
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.True);
            Assert.That(projections.LayoutRecorded, Is.False);
            projections.CommitRecordedLayout();
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.False);
            Update(layout, new Vector3(100, 0, 0));
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresFeedbackReset, Is.True);
            Assert.That(projections.Generation, Is.EqualTo(generation));
            for (int basis = 0; basis < 4; basis++)
            {
                Update(layout, basis == 3 ? new Vector3(0, 0, 4000) : Vector3.zero,
                    camera: basis == 0 ? 3ul : 1ul, light: basis == 1 ? 4ul : 2ul,
                    rotation: basis == 2 ? Quaternion.Euler(10, 20, 0) : Quaternion.identity);
                projections.PrepareClipmaps(layout);
                Assert.That(projections.RequiresFeedbackReset, Is.True);
                Assert.That(projections.Generation, Is.EqualTo(generation + 1));
            }
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

        [Test]
        public void ReceiverQualityChanges_DoNotRemapOrInvalidateRecordedCasterGeometry()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            using var projections = new VirtualShadowMapProjectionSet();
            Update(layout, Vector3.zero);
            projections.PrepareClipmaps(layout);
            projections.CommitRecordedLayout();
            ulong generation = projections.Generation;
            var buffer = projections.Buffer;
            layout.Update(Vector3.zero, Quaternion.identity, new Bounds(Vector3.zero, Vector3.one * 100),
                150, 512, 2, 3, 1, 2, transitionFraction: 0.4f);
            projections.PrepareClipmaps(layout);
            Assert.That(layout.NormalBias, Is.EqualTo(3));
            Assert.That(layout.BlendBorder, Is.EqualTo(0.2f));
            Assert.That(projections.Generation, Is.EqualTo(generation));
            Assert.That(projections.RequiresRemap, Is.False);
            Assert.That(projections.RequiresFeedbackReset, Is.False);
            Assert.That(projections.Buffer, Is.SameAs(buffer));
        }

        [TestCase(false, 512)]
        [TestCase(false, 4096)]
        [TestCase(true, 4096)]
        [TestCase(false, 16384)]
        public void ViewCoverage_RemainsNestedAcrossCameraAndProjectionChanges(bool orthographic, int resolution)
        {
            var go = new GameObject("VSM coverage test");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.orthographic = orthographic;
                var view = new VividCameraData { camera = camera };
                var focused = new VirtualShadowMapClipmapLayout();
                var baseline = new VirtualShadowMapClipmapLayout();
                var bounds = new Bounds(Vector3.zero, Vector3.one * 100);
                using var projections = new VirtualShadowMapProjectionSet();
                int pages = resolution / 128;
                for (int step = 0; step < 160; step++)
                {
                    Vector3 position = new Vector3(Mathf.Sin(step * .13f) * 7, step * .01f, Mathf.Cos(step * .07f) * 5);
                    camera.transform.SetPositionAndRotation(position, Quaternion.Euler(step * .3f, step * 7, 0));
                    camera.fieldOfView = 35 + step % 80;
                    camera.aspect = .6f + step % 17 * .1f;
                    camera.orthographicSize = 2 + step % 9;
                    camera.ResetProjectionMatrix();
                    var light = Quaternion.Euler(35, 23, 0);
                    baseline.Update(position, light, bounds, 150, resolution, 0, 1, 1, 2);
                    focused.Update(position, light, bounds, 150, resolution, 0, 1, 1, 2, coverageView: view);
                    Assert.That(focused.DepthMin, Is.EqualTo(baseline.DepthMin));
                    Assert.That(focused.DepthMax, Is.EqualTo(baseline.DepthMax));
                    Assert.That(focused.Views[0], Is.EqualTo(baseline.Views[0]));
                    Assert.That(focused.Views[focused.Count - 1], Is.EqualTo(baseline.Views[baseline.Count - 1]));
                    for (int i = 0; i + 1 < focused.Count; i++)
                    {
                        Assert.That(focused.OriginX[i] - focused.OriginX[i + 1] * 2, Is.InRange(1L, pages - 1L));
                        Assert.That(focused.OriginY[i] - focused.OriginY[i + 1] * 2, Is.InRange(1L, pages - 1L));
                        Assert.That(focused.Projections[i], Is.EqualTo(baseline.Projections[i]));
                    }
                    projections.PrepareClipmaps(focused);
                    projections.CommitRecordedLayout();
                }
                focused.Update(camera.transform.position, Quaternion.Euler(35, 23, 0), bounds,
                    150, resolution, 0, 1, 1, 2);
                for (int i = 0; i < focused.Count; i++)
                    Assert.That(focused.Views[i], Is.EqualTo(baseline.Views[i]));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ViewCoverage_ContextResetRetainsHysteresis()
        {
            var go = new GameObject("VSM coverage history test");
            try
            {
                var camera = go.AddComponent<Camera>();
                var view = new VividCameraData { camera = camera };
                var continuous = new VirtualShadowMapClipmapLayout();
                var resetEachFrame = new VirtualShadowMapClipmapLayout();
                var bounds = new Bounds(Vector3.zero, Vector3.one * 100);
                for (int frame = 0; frame < 400; frame++)
                {
                    camera.transform.SetPositionAndRotation(new Vector3(Mathf.Sin(frame * .1f) * .2f, 0, 0),
                        Quaternion.Euler(0, 40 + Mathf.Sin(frame * .03f) * 5, 0));
                    resetEachFrame.Reset();
                    continuous.Update(camera.transform.position, Quaternion.identity, bounds, 150, 4096, 0, 1, 1, 2, coverageView: view);
                    resetEachFrame.Update(camera.transform.position, Quaternion.identity, bounds, 150, 4096, 0, 1, 1, 2, coverageView: view);
                    CollectionAssert.AreEqual(continuous.OriginX, resetEachFrame.OriginX);
                    CollectionAssert.AreEqual(continuous.OriginY, resetEachFrame.OriginY);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ViewCoverage_StoppedCameraReturnsToCanonicalCoverage()
        {
            var go = new GameObject("VSM coverage stop test");
            try
            {
                var camera = go.AddComponent<Camera>();
                var view = new VividCameraData { camera = camera };
                var layout = new VirtualShadowMapClipmapLayout();
                var canonical = new VirtualShadowMapClipmapLayout();
                var bounds = new Bounds(Vector3.zero, Vector3.one * 100);
                for (int frame = 0; frame < 160; frame++)
                {
                    camera.transform.SetPositionAndRotation(new Vector3(Mathf.Sin(frame * .1f) * 2, 0, 0),
                        Quaternion.Euler(0, 40 + Mathf.Sin(frame * .03f) * 15, 0));
                    layout.Reset();
                    layout.Update(camera.transform.position, Quaternion.identity, bounds, 150, 4096, 0, 1, 1, 2, coverageView: view);
                    canonical.Update(camera.transform.position, Quaternion.identity, bounds, 150, 4096, 0, 1, 1, 2);
                    canonical.Update(camera.transform.position, Quaternion.identity, bounds, 150, 4096, 0, 1, 1, 2, coverageView: view);
                    layout.Reset();
                    layout.Update(camera.transform.position, Quaternion.identity, bounds, 150, 4096, 0, 1, 1, 2, coverageView: view);
                    CollectionAssert.AreEqual(canonical.OriginX, layout.OriginX);
                    CollectionAssert.AreEqual(canonical.OriginY, layout.OriginY);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ViewCoverage_StablePreparationAllocatesZeroAndPreservesRecordedDepth()
        {
            var go = new GameObject("VSM coverage allocation test");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.transform.rotation = Quaternion.Euler(0, 45, 0);
                var view = new VividCameraData { camera = camera };
                var layout = new VirtualShadowMapClipmapLayout();
                var bounds = new Bounds(Vector3.zero, Vector3.one * 100);
                using var projections = new VirtualShadowMapProjectionSet();
                for (int i = 0; i < 32; i++)
                {
                    layout.Reset();
                    layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 4096, 0, 1, 1, 2, coverageView: view);
                    projections.PrepareClipmaps(layout);
                    projections.CommitRecordedLayout();
                }
                var buffer = projections.Buffer;
                ulong generation = projections.Generation;
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                {
                    layout.Reset();
                    layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 4096, 0, 1, 1, 2, coverageView: view);
                    projections.PrepareClipmaps(layout);
                    projections.CommitRecordedLayout();
                }
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
                Assert.That(projections.Buffer, Is.SameAs(buffer));
                Assert.That(projections.Generation, Is.EqualTo(generation));
                Assert.That(projections.RequiresRemap, Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(256)]
        [TestCase(384)]
        [TestCase(512)]
        [TestCase(768)]
        [TestCase(1024)]
        public void PageBudget_ResizesAllPhysicalResourcesAndReusesTheStableConfiguration(int budget)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            try
            {
                VirtualShadowMapPrototypeRuntime.EnsureResources(4096, 10, 256);
                Assert.That(VirtualShadowMapPrototypeRuntime.EnsureResources(4096, 10, budget), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity, Is.EqualTo(budget));
                Assert.That(VirtualShadowMapPrototypeRuntime.RasterDepth.rt.volumeDepth, Is.EqualTo(budget));
                Assert.That(VirtualShadowMapPrototypeRuntime.PhysicalPageOwners.count, Is.EqualTo(budget));
                Assert.That(VirtualShadowMapPrototypeRuntime.StaticPhysicalPage.rt.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
                Assert.That(VirtualShadowMapPrototypeRuntime.StaticPhysicalPage.rt.volumeDepth, Is.EqualTo(VirtualShadowMapPrototypeRuntime.DepthLayerCount));
                Assert.That(VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage.rt.volumeDepth, Is.EqualTo(VirtualShadowMapPrototypeRuntime.DepthLayerCount));
                var pool = VirtualShadowMapPrototypeRuntime.StaticPhysicalPage;
                var workList = VirtualShadowMapPrototypeRuntime.PageWorkList;
                var workArgs = VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgs;
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
                Assert.That(VirtualShadowMapPrototypeRuntime.StaticPhysicalPage, Is.SameAs(pool));
                Assert.That(VirtualShadowMapPrototypeRuntime.PageWorkList, Is.SameAs(workList));
                Assert.That(VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgs, Is.SameAs(workArgs));
                Assert.That(VirtualShadowMapPrototypeRuntime.RemapPageMetadata, Is.SameAs(remapMetadata));
                Assert.That(VirtualShadowMapPrototypeRuntime.PageRequestFlags, Is.SameAs(requestFlags));
                VirtualShadowMapPrototypeRuntime.EnsureResources(4096, 10, 256);
                Assert.That(VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity, Is.EqualTo(256));
                Assert.That(VirtualShadowMapPrototypeRuntime.PageWorkList.count, Is.EqualTo(512));
            }
            finally { VirtualShadowMapPrototypeRuntime.ReleaseResources(); }
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
                // Eight levels exercise indexing above the legacy four-cascade limit.
                const int count = 128, capacity = 65;
                var tableData = new uint[count];
                var metadataData = new uint4[count];
                tableData[0] = 1;
                tableData[10] = 4;
                tableData[117] = 65; // Exercise the second physical dispatch group.
                for (int i = 0; i < count; i++)
                    metadataData[i] = new uint4(tableData[i] == 0 ? 0u : 10u, tableData[i], 7, 8);
                // Keep dirty, deferred, occupancy, age and debug state bit-for-bit.
                metadataData[0].x |= (1u << 2) | (1u << 17);
                metadataData[10].x |= (1u << 15) | (1u << 14);
                metadataData[117].w = 0x12345678u;
                // Unallocated feedback must not reappear as cached state after remap.
                metadataData[54] = new uint4(0u, 0u, 6u, 0xfeedu);
                using var remapMetadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 16);
                using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
                using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
                using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
                using var counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
                using var remap = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 16);
                using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (count + 31) / 32, 4);
                using var pressure = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 16);
                pressure.SetData(new uint4[3]);
                table.SetData(tableData);
                metadata.SetData(metadataData);
                var ownersData = new uint[capacity];
                for (int page = 0; page < count; page++)
                    if (tableData[page] != 0u) ownersData[tableData[page] - 1u] = (uint)page + 1u;
                owners.SetData(ownersData);
                var remaps = new int4[8];
                for (int i = 0; i < 8; i++) remaps[i] = new int4(dx, dy, resetBasis ? 1 : 0, 0);
                remap.SetData(remaps);
                if (mixedLevels)
                {
                    remaps[0] = int4.zero;
                    remaps[7] = new int4(-1, 0, 0, 0);
                    remap.SetData(remaps);
                }
                int update = shader.FindKernel("VSMUpdatePhysicalPageAddresses");
                int clear = shader.FindKernel("VSMClearVirtualPageMappings");
                int move = shader.FindKernel("VSMRemapPages");
                int allocate = shader.FindKernel("VSMPrototypeAllocatePages");
                shader.SetInt("_VSMProjectionCount", 8);
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
