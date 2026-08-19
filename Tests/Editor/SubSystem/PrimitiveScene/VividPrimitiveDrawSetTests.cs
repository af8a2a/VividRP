using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Editor.Tests
{
    public sealed class VividPrimitiveDrawSetTests
    {
        [Test]
        public void DrawSetLayouts_HaveExpectedStrides()
        {
            Assert.That(UnsafeUtility.SizeOf<VividPrimitiveDrawSetEntry>(), Is.EqualTo(16));
            Assert.That(UnsafeUtility.SizeOf<VividPrimitiveDrawBucket>(), Is.EqualTo(16));
            Assert.That(UnsafeUtility.SizeOf<VividPrimitiveDrawSetBuildResult>(), Is.EqualTo(16));
        }

        [Test]
        public void FrustumCullJob_FiltersStatePassLayerAndBounds_WhileKeepingSkinnedConservative()
        {
            using NativeArray<float4> planes = CreateBoxFrustumPlanes(5.0f);
            using var records = new NativeArray<VividPrimitiveCullRecord>(6, Allocator.TempJob)
            {
                [0] = CreateRecord(CreateHandle(0), new float3(-1.0f), new float3(1.0f)),
                [1] = CreateRecord(CreateHandle(1), new float3(9.0f, -1.0f, -1.0f), new float3(11.0f, 1.0f, 1.0f)),
                [2] = CreateRecord(
                    CreateHandle(2),
                    new float3(-1.0f),
                    new float3(1.0f),
                    flags: VividPrimitiveFlags.Valid | VividPrimitiveFlags.Disabled),
                [3] = CreateRecord(
                    CreateHandle(3),
                    new float3(-1.0f),
                    new float3(1.0f),
                    passMask: VividInstancePassMask.Shadows),
                [4] = CreateRecord(
                    CreateHandle(4),
                    new float3(-1.0f),
                    new float3(1.0f),
                    cameraLayerMask: 1u << 1),
                [5] = CreateRecord(
                    CreateHandle(5),
                    new float3(99.0f),
                    new float3(101.0f),
                    flags: VividPrimitiveFlags.Valid | VividPrimitiveFlags.Skinned),
            };
            using var visibility = new NativeArray<byte>(records.Length, Allocator.TempJob);

            new VividPrimitiveFrustumCullJob
            {
                CullingRecords = records,
                FrustumPlanes = planes,
                Visibility = visibility,
                CameraCullingMask = 1u,
            }.Schedule(records.Length, 1).Complete();

            Assert.That(visibility.ToArray(), Is.EqualTo(new byte[] { 1, 0, 0, 0, 0, 1 }));
        }

        [Test]
        public void FrustumCullJob_KeepsAabbTouchingPlane_AndRejectsAabbBeyondPlane()
        {
            using NativeArray<float4> planes = CreateBoxFrustumPlanes(5.0f);
            using var records = new NativeArray<VividPrimitiveCullRecord>(2, Allocator.TempJob)
            {
                [0] = CreateRecord(
                    CreateHandle(0),
                    new float3(5.0f, -1.0f, -1.0f),
                    new float3(6.0f, 1.0f, 1.0f)),
                [1] = CreateRecord(
                    CreateHandle(1),
                    new float3(5.01f, -1.0f, -1.0f),
                    new float3(6.0f, 1.0f, 1.0f)),
            };
            using var visibility = new NativeArray<byte>(records.Length, Allocator.TempJob);

            new VividPrimitiveFrustumCullJob
            {
                CullingRecords = records,
                FrustumPlanes = planes,
                Visibility = visibility,
                CameraCullingMask = uint.MaxValue,
            }.Schedule(records.Length, 1).Complete();

            Assert.That(visibility[0], Is.EqualTo(1));
            Assert.That(visibility[1], Is.Zero);
        }

        [Test]
        public void BuildDrawSetJob_BucketsVisibleSourcesAndFlipsWinding()
        {
            VividPrimitiveHandle firstHandle = CreateHandle(3);
            VividPrimitiveHandle secondHandle = CreateHandle(9);
            using var records = new NativeArray<VividPrimitiveCullRecord>(2, Allocator.TempJob)
            {
                [0] = CreateRecord(
                    firstHandle,
                    new float3(-1.0f),
                    new float3(1.0f),
                    drawSectionOffset: 0u,
                    drawSectionCount: 4u,
                    flags: VividPrimitiveFlags.Valid | VividPrimitiveFlags.FlipWindingOrder),
                [1] = CreateRecord(
                    secondHandle,
                    new float3(-1.0f),
                    new float3(1.0f),
                    drawSectionOffset: 4u,
                    drawSectionCount: 2u),
            };
            using var visibility = new NativeArray<byte>(new byte[] { 1, 0 }, Allocator.TempJob);
            using var sources = new NativeArray<VividPrimitiveDrawSourceData>(6, Allocator.TempJob)
            {
                [0] = CreateSource(firstHandle, 0, 10, VividRendererListID.Default),
                [1] = CreateSource(firstHandle, 1, 11, VividRendererListID.CullFront),
                [2] = CreateSource(firstHandle, 2, 12, VividRendererListID.CullOff),
                [3] = CreateSource(firstHandle, 3, 13, VividRendererListID.AlphaTest),
                [4] = CreateSource(secondHandle, 4, 20, VividRendererListID.Default),
                [5] = CreateSource(secondHandle, 5, 21, VividRendererListID.Default),
            };
            using var entries = new NativeArray<VividPrimitiveDrawSetEntry>(sources.Length, Allocator.TempJob);
            using var legacyIndices = new NativeArray<uint>(sources.Length, Allocator.TempJob);
            using var buckets = new NativeArray<VividPrimitiveDrawBucket>(
                VividPrimitiveBuildDrawSetJob.RendererListCount,
                Allocator.TempJob);
            using var counts = new NativeArray<int>(buckets.Length, Allocator.TempJob);
            using var cursors = new NativeArray<int>(buckets.Length, Allocator.TempJob);
            using var result = new NativeArray<VividPrimitiveDrawSetBuildResult>(1, Allocator.TempJob);

            new VividPrimitiveBuildDrawSetJob
            {
                CullingRecords = records,
                Visibility = visibility,
                DrawSources = sources,
                Entries = entries,
                LegacyInstanceIndices = legacyIndices,
                Buckets = buckets,
                BucketCounts = counts,
                BucketWriteCursors = cursors,
                Result = result,
            }.Schedule().Complete();

            Assert.That(result[0].VisiblePrimitiveCount, Is.EqualTo(1));
            Assert.That(result[0].DrawCount, Is.EqualTo(4));
            Assert.That(result[0].NonEmptyBucketCount, Is.EqualTo(4));
            AssertBucket(buckets[(int) VividRendererListID.Default], 0, 1);
            AssertBucket(buckets[(int) VividRendererListID.CullFront], 1, 1);
            AssertBucket(buckets[(int) VividRendererListID.CullOff], 2, 1);
            AssertBucket(buckets[(int) (VividRendererListID.AlphaTest | VividRendererListID.CullFront)], 3, 1);
            Assert.That(legacyIndices.GetSubArray(0, 4).ToArray(), Is.EqualTo(new uint[] { 11, 10, 12, 13 }));
            Assert.That(entries[0].PrimitiveIndex, Is.EqualTo((uint) firstHandle.Index));
            Assert.That(entries[0].PrimitiveGeneration, Is.EqualTo(firstHandle.Generation));
            Assert.That(entries[0].DrawSectionIndex, Is.EqualTo(1u));
            Assert.That(entries[0].LegacyInstanceIndex, Is.EqualTo(11u));
        }

        [Test]
        public void BuildDrawSetJob_RejectsInvalidStaleAndUnbridgedSources()
        {
            VividPrimitiveHandle handle = CreateHandle(0);
            using var records = new NativeArray<VividPrimitiveCullRecord>(1, Allocator.TempJob)
            {
                [0] = CreateRecord(
                    handle,
                    new float3(-1.0f),
                    new float3(1.0f),
                    drawSectionCount: 4u),
            };
            using var visibility = new NativeArray<byte>(new byte[] { 1 }, Allocator.TempJob);
            using var sources = new NativeArray<VividPrimitiveDrawSourceData>(4, Allocator.TempJob)
            {
                [0] = CreateSource(handle, 0, 7, VividRendererListID.Default),
                [1] = CreateSource(CreateHandle(0, generation: 2u), 1, 8, VividRendererListID.Default),
                [2] = CreateSource(handle, 2, uint.MaxValue, VividRendererListID.Default),
                [3] = CreateSource(handle, 3, 9, VividRendererListID.Default, VividPrimitiveDrawSourceFlags.None),
            };
            using var entries = new NativeArray<VividPrimitiveDrawSetEntry>(sources.Length, Allocator.TempJob);
            using var legacyIndices = new NativeArray<uint>(sources.Length, Allocator.TempJob);
            using var buckets = new NativeArray<VividPrimitiveDrawBucket>(
                VividPrimitiveBuildDrawSetJob.RendererListCount,
                Allocator.TempJob);
            using var counts = new NativeArray<int>(buckets.Length, Allocator.TempJob);
            using var cursors = new NativeArray<int>(buckets.Length, Allocator.TempJob);
            using var result = new NativeArray<VividPrimitiveDrawSetBuildResult>(1, Allocator.TempJob);

            new VividPrimitiveBuildDrawSetJob
            {
                CullingRecords = records,
                Visibility = visibility,
                DrawSources = sources,
                Entries = entries,
                LegacyInstanceIndices = legacyIndices,
                Buckets = buckets,
                BucketCounts = counts,
                BucketWriteCursors = cursors,
                Result = result,
            }.Schedule().Complete();

            Assert.That(result[0].DrawCount, Is.EqualTo(1));
            Assert.That(legacyIndices[0], Is.EqualTo(7u));
        }

        [Test]
        public void DrawSet_BuildsGpuIndexBufferByDrawCount_AndDoesNotShrink()
        {
            using NativeArray<float4> planes = CreateBoxFrustumPlanes(5.0f);
            VividPrimitiveHandle handle = CreateHandle(0);
            using var records = new NativeArray<VividPrimitiveCullRecord>(1, Allocator.TempJob)
            {
                [0] = CreateRecord(
                    handle,
                    new float3(-1.0f),
                    new float3(1.0f),
                    drawSectionCount: 3u),
            };
            using var sources = new NativeArray<VividPrimitiveDrawSourceData>(3, Allocator.TempJob)
            {
                [0] = CreateSource(handle, 0, 101, VividRendererListID.Default),
                [1] = CreateSource(handle, 1, 102, VividRendererListID.Default),
                [2] = CreateSource(handle, 2, 103, VividRendererListID.Default),
            };
            using var emptyRecords = new NativeArray<VividPrimitiveCullRecord>(0, Allocator.TempJob);
            using var emptySources = new NativeArray<VividPrimitiveDrawSourceData>(0, Allocator.TempJob);
            var drawSet = new VividPrimitiveDrawSet();
            try
            {
                drawSet.Build(planes, 1u, records, sources, sceneRevision: 7u, frameIndex: 12);

                Assert.That(drawSet.DrawCount, Is.EqualTo(3));
                Assert.That(drawSet.LegacyInstanceIndexBuffer, Is.Not.Null);
                Assert.That(drawSet.LegacyInstanceIndexBuffer.count, Is.EqualTo(4));
                var uploaded = new uint[4];
                drawSet.LegacyInstanceIndexBuffer.GetData(uploaded);
                Assert.That(uploaded[0], Is.EqualTo(101u));
                Assert.That(uploaded[1], Is.EqualTo(102u));
                Assert.That(uploaded[2], Is.EqualTo(103u));
                VividPrimitiveDrawSetStats populatedStats = drawSet.GetStats();
                Assert.That(populatedStats.UploadCount, Is.EqualTo(1));
                Assert.That(populatedStats.UploadBytes, Is.EqualTo(3 * sizeof(uint)));
                Assert.That(populatedStats.DrawCapacity, Is.EqualTo(4));
                Assert.That(populatedStats.SceneRevision, Is.EqualTo(7u));
                Assert.That(drawSet.TryGetBucket(
                    VividRendererListID.Default,
                    out VividPrimitiveDrawBucket defaultBucket), Is.True);
                Assert.That(defaultBucket.DrawOffset, Is.Zero);
                Assert.That(defaultBucket.DrawCount, Is.EqualTo(3u));
                Assert.That(drawSet.TryGetBucket(
                    VividRendererListID.AlphaTest,
                    out _), Is.False);

                drawSet.Build(planes, 1u, emptyRecords, emptySources, sceneRevision: 8u, frameIndex: 13);

                Assert.That(drawSet.DrawCount, Is.Zero);
                Assert.That(drawSet.LegacyInstanceIndexBuffer.count, Is.EqualTo(4));
                Assert.That(drawSet.GetStats().UploadCount, Is.Zero);
                Assert.That(drawSet.GetStats().UploadBytes, Is.Zero);
            }
            finally
            {
                drawSet.Dispose();
            }
        }

        [Test]
        public void DrawSetSystem_KeepsIndependentPersistentStatePerCamera()
        {
            using var records = new NativeArray<VividPrimitiveCullRecord>(0, Allocator.TempJob);
            using var sources = new NativeArray<VividPrimitiveDrawSourceData>(0, Allocator.TempJob);
            using var system = new VividPrimitiveDrawSetSystem();
            GameObject firstCameraObject = null;
            GameObject secondCameraObject = null;
            try
            {
                firstCameraObject = new GameObject("Primitive DrawSet Camera A");
                secondCameraObject = new GameObject("Primitive DrawSet Camera B");
                Camera firstCamera = firstCameraObject.AddComponent<Camera>();
                Camera secondCamera = secondCameraObject.AddComponent<Camera>();

                VividPrimitiveDrawSet first = system.Build(
                    firstCamera,
                    records,
                    sources,
                    sceneRevision: 3u,
                    frameIndex: 10);
                VividPrimitiveDrawSet second = system.Build(
                    secondCamera,
                    records,
                    sources,
                    sceneRevision: 4u,
                    frameIndex: 11);

                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(first.LegacyInstanceIndexBuffer, Is.Not.SameAs(
                    second.LegacyInstanceIndexBuffer));
                Assert.That(first.FrameIndex, Is.EqualTo(10));
                Assert.That(second.FrameIndex, Is.EqualTo(11));
                Assert.That(system.TryGet(firstCamera, out VividPrimitiveDrawSet resolvedFirst), Is.True);
                Assert.That(system.TryGet(secondCamera, out VividPrimitiveDrawSet resolvedSecond), Is.True);
                Assert.That(resolvedFirst, Is.SameAs(first));
                Assert.That(resolvedSecond, Is.SameAs(second));
            }
            finally
            {
                if (firstCameraObject != null)
                    Object.DestroyImmediate(firstCameraObject);
                if (secondCameraObject != null)
                    Object.DestroyImmediate(secondCameraObject);
            }
        }

        private static NativeArray<float4> CreateBoxFrustumPlanes(float halfExtent)
        {
            return new NativeArray<float4>(new[]
            {
                new float4(1.0f, 0.0f, 0.0f, halfExtent),
                new float4(-1.0f, 0.0f, 0.0f, halfExtent),
                new float4(0.0f, 1.0f, 0.0f, halfExtent),
                new float4(0.0f, -1.0f, 0.0f, halfExtent),
                new float4(0.0f, 0.0f, 1.0f, halfExtent),
                new float4(0.0f, 0.0f, -1.0f, halfExtent),
            }, Allocator.TempJob);
        }

        private static VividPrimitiveHandle CreateHandle(
            int index,
            uint generation = 1u,
            uint sceneToken = 1u)
        {
            return new VividPrimitiveHandle(index, generation, sceneToken);
        }

        private static VividPrimitiveCullRecord CreateRecord(
            VividPrimitiveHandle handle,
            float3 boundsMin,
            float3 boundsMax,
            uint drawSectionOffset = 0u,
            uint drawSectionCount = 0u,
            VividInstancePassMask passMask = VividInstancePassMask.Main,
            VividPrimitiveFlags flags = VividPrimitiveFlags.Valid,
            uint cameraLayerMask = 1u)
        {
            return new VividPrimitiveCullRecord
            {
                Handle = handle,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                DrawSectionOffset = drawSectionOffset,
                DrawSectionCount = drawSectionCount,
                PassMask = passMask,
                Flags = flags,
                CameraLayerMask = cameraLayerMask,
            };
        }

        private static VividPrimitiveDrawSourceData CreateSource(
            VividPrimitiveHandle handle,
            uint absoluteSectionIndex,
            uint legacyInstanceIndex,
            VividRendererListID rendererListID,
            VividPrimitiveDrawSourceFlags flags = VividPrimitiveDrawSourceFlags.Valid)
        {
            return new VividPrimitiveDrawSourceData
            {
                PrimitiveHandle = handle,
                AbsoluteDrawSectionIndex = absoluteSectionIndex,
                LegacyInstanceIndex = legacyInstanceIndex,
                RendererListID = rendererListID,
                Flags = flags,
            };
        }

        private static void AssertBucket(
            in VividPrimitiveDrawBucket bucket,
            uint expectedOffset,
            uint expectedCount)
        {
            Assert.That(bucket.DrawOffset, Is.EqualTo(expectedOffset));
            Assert.That(bucket.DrawCount, Is.EqualTo(expectedCount));
        }
    }
}
