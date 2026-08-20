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
                RequiredPassMask = VividInstancePassMask.Main,
                FrustumCount = 1,
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
                RequiredPassMask = VividInstancePassMask.Main,
                FrustumCount = 1,
            }.Schedule(records.Length, 1).Complete();

            Assert.That(visibility[0], Is.EqualTo(1));
            Assert.That(visibility[1], Is.Zero);
        }

        [Test]
        public void FrustumCullJob_UsesRequiredPassMaskForMainAndShadow()
        {
            using NativeArray<float4> planes = CreateBoxFrustumPlanes(5.0f);
            using var records = new NativeArray<VividPrimitiveCullRecord>(3, Allocator.TempJob)
            {
                [0] = CreateRecord(
                    CreateHandle(0),
                    new float3(-1.0f),
                    new float3(1.0f),
                    passMask: VividInstancePassMask.Main),
                [1] = CreateRecord(
                    CreateHandle(1),
                    new float3(-1.0f),
                    new float3(1.0f),
                    passMask: VividInstancePassMask.Shadows),
                [2] = CreateRecord(
                    CreateHandle(2),
                    new float3(-1.0f),
                    new float3(1.0f),
                    passMask: VividInstancePassMask.Main | VividInstancePassMask.Shadows),
            };
            using var visibility = new NativeArray<byte>(records.Length, Allocator.TempJob);

            new VividPrimitiveFrustumCullJob
            {
                CullingRecords = records,
                FrustumPlanes = planes,
                Visibility = visibility,
                CameraCullingMask = uint.MaxValue,
                RequiredPassMask = VividInstancePassMask.Shadows,
                FrustumCount = 1,
            }.Schedule(records.Length, 1).Complete();

            Assert.That(visibility.ToArray(), Is.EqualTo(new byte[] { 0, 1, 1 }));

            new VividPrimitiveFrustumCullJob
            {
                CullingRecords = records,
                FrustumPlanes = planes,
                Visibility = visibility,
                CameraCullingMask = uint.MaxValue,
                RequiredPassMask = VividInstancePassMask.Main,
                FrustumCount = 1,
            }.Schedule(records.Length, 1).Complete();

            Assert.That(visibility.ToArray(), Is.EqualTo(new byte[] { 1, 0, 1 }));
        }

        [Test]
        public void ShadowDrawSet_UnionsCascadeFrusta()
        {
            var cameraObject = new GameObject("Shadow Primitive DrawSet Camera");
            using var drawSet = new VividPrimitiveDrawSet();
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                Matrix4x4 projection = Matrix4x4.Ortho(-2.0f, 2.0f, -2.0f, 2.0f, -2.0f, 2.0f);
                var viewMatrices = new[]
                {
                    Matrix4x4.identity,
                    Matrix4x4.Translate(new Vector3(-10.0f, 0.0f, 0.0f)),
                };
                var projectionMatrices = new[] { projection, projection };
                var firstBounds = new Bounds(Vector3.zero, Vector3.one);
                var secondBounds = new Bounds(new Vector3(10.0f, 0.0f, 0.0f), Vector3.one);
                var rejectedBounds = new Bounds(new Vector3(5.0f, 0.0f, 0.0f), Vector3.one);

                Plane[] firstPlanes = GeometryUtility.CalculateFrustumPlanes(
                    projectionMatrices[0] * viewMatrices[0]);
                Plane[] secondPlanes = GeometryUtility.CalculateFrustumPlanes(
                    projectionMatrices[1] * viewMatrices[1]);
                Assert.That(GeometryUtility.TestPlanesAABB(firstPlanes, firstBounds), Is.True);
                Assert.That(GeometryUtility.TestPlanesAABB(secondPlanes, firstBounds), Is.False);
                Assert.That(GeometryUtility.TestPlanesAABB(firstPlanes, secondBounds), Is.False);
                Assert.That(GeometryUtility.TestPlanesAABB(secondPlanes, secondBounds), Is.True);
                Assert.That(GeometryUtility.TestPlanesAABB(firstPlanes, rejectedBounds), Is.False);
                Assert.That(GeometryUtility.TestPlanesAABB(secondPlanes, rejectedBounds), Is.False);

                VividPrimitiveHandle firstHandle = CreateHandle(0);
                VividPrimitiveHandle secondHandle = CreateHandle(1);
                VividPrimitiveHandle rejectedHandle = CreateHandle(2);
                using var records = new NativeArray<VividPrimitiveCullRecord>(3, Allocator.TempJob)
                {
                    [0] = CreateRecord(
                        firstHandle,
                        firstBounds.min,
                        firstBounds.max,
                        drawSectionOffset: 0u,
                        drawSectionCount: 1u,
                        passMask: VividInstancePassMask.Shadows),
                    [1] = CreateRecord(
                        secondHandle,
                        secondBounds.min,
                        secondBounds.max,
                        drawSectionOffset: 1u,
                        drawSectionCount: 1u,
                        passMask: VividInstancePassMask.Shadows),
                    [2] = CreateRecord(
                        rejectedHandle,
                        rejectedBounds.min,
                        rejectedBounds.max,
                        drawSectionOffset: 2u,
                        drawSectionCount: 1u,
                        passMask: VividInstancePassMask.Shadows),
                };
                using var sources = new NativeArray<VividPrimitiveDrawSourceData>(3, Allocator.TempJob)
                {
                    [0] = CreateSource(firstHandle, 0, 101, VividRendererListID.Default),
                    [1] = CreateSource(secondHandle, 1, 102, VividRendererListID.Default),
                    [2] = CreateSource(rejectedHandle, 2, 103, VividRendererListID.Default),
                };

                drawSet.Schedule(
                    camera,
                    viewMatrices,
                    projectionMatrices,
                    frustumCount: 2,
                    requiredPassMask: VividInstancePassMask.Shadows,
                    cullAgainstNearPlane: true,
                    records,
                    sources,
                    sceneRevision: 6u,
                    frameIndex: 17);
                drawSet.CompleteScheduledBuild();

                Assert.That(drawSet.VisiblePrimitiveCount, Is.EqualTo(2));
                Assert.That(drawSet.DrawCount, Is.EqualTo(2));
                Assert.That(drawSet.LegacyInstanceIndices.ToArray(), Is.EqualTo(new uint[] { 101, 102 }));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ShadowDrawSet_CanIgnoreRasterNearPlaneForPancakedCasters()
        {
            var cameraObject = new GameObject("Near Plane Shadow Primitive DrawSet Camera");
            using var drawSet = new VividPrimitiveDrawSet();
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                Matrix4x4 projection = Matrix4x4.Ortho(-5.0f, 5.0f, -5.0f, 5.0f, -5.0f, 5.0f);
                var viewMatrices = new[] { Matrix4x4.identity };
                var projectionMatrices = new[] { projection };
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(projection);
                Plane nearPlane = planes[4];
                Vector3 pointOnNearPlane = -nearPlane.normal * nearPlane.distance;
                var casterBounds = new Bounds(
                    pointOnNearPlane - nearPlane.normal,
                    Vector3.one * 0.1f);
                var planesWithoutNear = new[] { planes[0], planes[1], planes[2], planes[3], planes[5] };
                Assert.That(GeometryUtility.TestPlanesAABB(planes, casterBounds), Is.False);
                Assert.That(GeometryUtility.TestPlanesAABB(planesWithoutNear, casterBounds), Is.True);

                VividPrimitiveHandle handle = CreateHandle(0);
                using var records = new NativeArray<VividPrimitiveCullRecord>(1, Allocator.TempJob)
                {
                    [0] = CreateRecord(
                        handle,
                        casterBounds.min,
                        casterBounds.max,
                        drawSectionCount: 1u,
                        passMask: VividInstancePassMask.Shadows),
                };
                using var sources = new NativeArray<VividPrimitiveDrawSourceData>(1, Allocator.TempJob)
                {
                    [0] = CreateSource(handle, 0, 77, VividRendererListID.Default),
                };

                drawSet.Schedule(
                    camera,
                    viewMatrices,
                    projectionMatrices,
                    frustumCount: 1,
                    requiredPassMask: VividInstancePassMask.Shadows,
                    cullAgainstNearPlane: false,
                    records,
                    sources,
                    sceneRevision: 8u,
                    frameIndex: 18);
                drawSet.CompleteScheduledBuild();
                Assert.That(drawSet.DrawCount, Is.EqualTo(1));

                drawSet.Schedule(
                    camera,
                    viewMatrices,
                    projectionMatrices,
                    frustumCount: 1,
                    requiredPassMask: VividInstancePassMask.Shadows,
                    cullAgainstNearPlane: true,
                    records,
                    sources,
                    sceneRevision: 8u,
                    frameIndex: 19);
                drawSet.CompleteScheduledBuild();
                Assert.That(drawSet.DrawCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
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
        public void DrawSet_ScheduleDefersCompletionAndUpload_AndRepeatedSchedulePublishesLatestBuild()
        {
            using NativeArray<float4> firstPlanes = CreateBoxFrustumPlanes(5.0f);
            using NativeArray<float4> secondPlanes = CreateBoxFrustumPlanes(10.0f);
            VividPrimitiveHandle firstHandle = CreateHandle(0);
            VividPrimitiveHandle secondHandle = CreateHandle(1);
            using var firstRecords = new NativeArray<VividPrimitiveCullRecord>(1, Allocator.TempJob)
            {
                [0] = CreateRecord(
                    firstHandle,
                    new float3(-1.0f),
                    new float3(1.0f),
                    drawSectionCount: 1u),
            };
            using var firstSources = new NativeArray<VividPrimitiveDrawSourceData>(1, Allocator.TempJob)
            {
                [0] = CreateSource(firstHandle, 0, 101, VividRendererListID.Default),
            };
            using var secondRecords = new NativeArray<VividPrimitiveCullRecord>(1, Allocator.TempJob)
            {
                [0] = CreateRecord(
                    secondHandle,
                    new float3(-1.0f),
                    new float3(1.0f),
                    drawSectionCount: 3u),
            };
            using var secondSources = new NativeArray<VividPrimitiveDrawSourceData>(3, Allocator.TempJob)
            {
                [0] = CreateSource(secondHandle, 0, 201, VividRendererListID.Default),
                [1] = CreateSource(secondHandle, 1, 202, VividRendererListID.Default),
                [2] = CreateSource(secondHandle, 2, 203, VividRendererListID.Default),
            };
            var drawSet = new VividPrimitiveDrawSet();
            try
            {
                drawSet.Schedule(
                    firstPlanes,
                    1u,
                    firstRecords,
                    firstSources,
                    sceneRevision: 4u,
                    frameIndex: 20);

                Assert.That(drawSet.HasPendingBuild, Is.True);
                Assert.That(drawSet.IsBuilt, Is.False);
                Assert.That(drawSet.MatchesPendingBuild(4u), Is.True);
                Assert.That(drawSet.MatchesPendingBuild(4u, 20), Is.True);
                Assert.That(drawSet.GetStats().UploadCount, Is.Zero);
                Assert.That(drawSet.LegacyInstanceIndexBuffer, Is.Null);

                // Grow the output while the first build may still be running. The
                // second build is chained without completing or uploading the first.
                drawSet.Schedule(
                    secondPlanes,
                    1u,
                    secondRecords,
                    secondSources,
                    sceneRevision: 5u,
                    frameIndex: 21);

                Assert.That(drawSet.HasPendingBuild, Is.True);
                Assert.That(drawSet.IsBuilt, Is.False);
                Assert.That(drawSet.MatchesPendingBuild(4u), Is.False);
                Assert.That(drawSet.MatchesPendingBuild(5u), Is.True);
                Assert.That(drawSet.MatchesPendingBuild(5u, 21), Is.True);
                Assert.That(drawSet.GetStats().UploadCount, Is.Zero);

                Assert.That(drawSet.CompleteScheduledBuild(), Is.True);

                Assert.That(drawSet.HasPendingBuild, Is.False);
                Assert.That(drawSet.IsBuilt, Is.True);
                Assert.That(drawSet.DrawCount, Is.EqualTo(3));
                Assert.That(drawSet.GetStats().UploadCount, Is.EqualTo(1));
                var uploaded = new uint[drawSet.LegacyInstanceIndexBuffer.count];
                drawSet.LegacyInstanceIndexBuffer.GetData(uploaded);
                Assert.That(uploaded[0], Is.EqualTo(201u));
                Assert.That(uploaded[1], Is.EqualTo(202u));
                Assert.That(uploaded[2], Is.EqualTo(203u));

                Assert.That(drawSet.CompleteScheduledBuild(), Is.True);
                Assert.That(drawSet.GetStats().UploadCount, Is.EqualTo(1));
            }
            finally
            {
                drawSet.Dispose();
            }
        }

        [Test]
        public void DrawSet_CameraScheduleUsesGuardBandForLaterTemporalJitter()
        {
            var cameraObject = new GameObject("Guard Band Primitive DrawSet Camera");
            var target = new RenderTexture(100, 100, 0);
            var drawSet = new VividPrimitiveDrawSet();
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5.0f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100.0f;
                camera.targetTexture = target;

                var boundaryBounds = new Bounds(
                    new Vector3(5.2f, 0.0f, 5.0f),
                    new Vector3(0.1f, 0.1f, 0.1f));
                Plane[] originalPlanes = GeometryUtility.CalculateFrustumPlanes(
                    camera.projectionMatrix * camera.worldToCameraMatrix);
                Assert.That(
                    GeometryUtility.TestPlanesAABB(originalPlanes, boundaryBounds),
                    Is.False,
                    "The test bound must start outside the unexpanded camera frustum.");

                VividPrimitiveHandle handle = CreateHandle(0);
                Vector3 boundsMin = boundaryBounds.min;
                Vector3 boundsMax = boundaryBounds.max;
                using var records = new NativeArray<VividPrimitiveCullRecord>(1, Allocator.TempJob)
                {
                    [0] = CreateRecord(handle, boundsMin, boundsMax, drawSectionCount: 1u),
                };
                using var sources = new NativeArray<VividPrimitiveDrawSourceData>(1, Allocator.TempJob)
                {
                    [0] = CreateSource(handle, 0, 44, VividRendererListID.Default),
                };

                drawSet.Schedule(camera, records, sources, sceneRevision: 1u, frameIndex: 1);
                drawSet.CompleteScheduledBuild();

                Assert.That(drawSet.DrawCount, Is.EqualTo(1));
                Assert.That(drawSet.LegacyInstanceIndices[0], Is.EqualTo(44u));
            }
            finally
            {
                drawSet.Dispose();
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void DrawSet_CameraScheduleUsesNonJitteredProjectionAsItsGuardBandBase()
        {
            var cameraObject = new GameObject("Non-Jittered Primitive DrawSet Camera");
            var target = new RenderTexture(100, 100, 0);
            var drawSet = new VividPrimitiveDrawSet();
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5.0f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100.0f;
                camera.targetTexture = target;

                Matrix4x4 nonJitteredProjection = camera.projectionMatrix;
                Matrix4x4 largeJitter = Matrix4x4.identity;
                largeJitter.m03 = 0.5f;
                camera.nonJitteredProjectionMatrix = nonJitteredProjection;
                camera.projectionMatrix = largeJitter * nonJitteredProjection;

                var bounds = new Bounds(
                    new Vector3(4.5f, 0.0f, 5.0f),
                    new Vector3(0.1f, 0.1f, 0.1f));
                Assert.That(
                    GeometryUtility.TestPlanesAABB(
                        GeometryUtility.CalculateFrustumPlanes(
                            camera.projectionMatrix * camera.worldToCameraMatrix),
                        bounds),
                    Is.False,
                    "The current jittered projection must reject the test bound.");
                Assert.That(
                    GeometryUtility.TestPlanesAABB(
                        GeometryUtility.CalculateFrustumPlanes(
                            nonJitteredProjection * camera.worldToCameraMatrix),
                        bounds),
                    Is.True,
                    "The non-jittered projection must contain the test bound.");

                VividPrimitiveHandle handle = CreateHandle(0);
                Vector3 boundsMin = bounds.min;
                Vector3 boundsMax = bounds.max;
                using var records = new NativeArray<VividPrimitiveCullRecord>(1, Allocator.TempJob)
                {
                    [0] = CreateRecord(handle, boundsMin, boundsMax, drawSectionCount: 1u),
                };
                using var sources = new NativeArray<VividPrimitiveDrawSourceData>(1, Allocator.TempJob)
                {
                    [0] = CreateSource(handle, 0, 45, VividRendererListID.Default),
                };

                drawSet.Build(camera, records, sources, sceneRevision: 1u, frameIndex: 1);

                Assert.That(drawSet.DrawCount, Is.EqualTo(1));
                Assert.That(drawSet.LegacyInstanceIndices[0], Is.EqualTo(45u));
            }
            finally
            {
                drawSet.Dispose();
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void DrawSet_DisposeCompletesPendingJobsWithoutUploading()
        {
            using NativeArray<float4> planes = CreateBoxFrustumPlanes(5.0f);
            VividPrimitiveHandle handle = CreateHandle(0);
            using var records = new NativeArray<VividPrimitiveCullRecord>(1, Allocator.TempJob)
            {
                [0] = CreateRecord(
                    handle,
                    new float3(-1.0f),
                    new float3(1.0f),
                    drawSectionCount: 1u),
            };
            using var sources = new NativeArray<VividPrimitiveDrawSourceData>(1, Allocator.TempJob)
            {
                [0] = CreateSource(handle, 0, 31, VividRendererListID.Default),
            };
            var drawSet = new VividPrimitiveDrawSet();

            drawSet.Schedule(planes, 1u, records, sources, sceneRevision: 1u, frameIndex: 2);

            Assert.That(drawSet.HasPendingBuild, Is.True);
            Assert.DoesNotThrow(drawSet.Dispose);
            Assert.That(drawSet.HasPendingBuild, Is.False);
            Assert.That(drawSet.IsBuilt, Is.False);
            Assert.That(drawSet.LegacyInstanceIndexBuffer, Is.Null);
        }

        [Test]
        public void DrawSet_CompleteAndInvalidateDiscardsPendingBuildWithoutUploading()
        {
            using NativeArray<float4> planes = CreateBoxFrustumPlanes(5.0f);
            VividPrimitiveHandle handle = CreateHandle(0);
            using var records = new NativeArray<VividPrimitiveCullRecord>(1, Allocator.TempJob)
            {
                [0] = CreateRecord(
                    handle,
                    new float3(-1.0f),
                    new float3(1.0f),
                    drawSectionCount: 1u),
            };
            using var sources = new NativeArray<VividPrimitiveDrawSourceData>(1, Allocator.TempJob)
            {
                [0] = CreateSource(handle, 0, 61, VividRendererListID.Default),
            };
            using var drawSet = new VividPrimitiveDrawSet();
            drawSet.Schedule(planes, 1u, records, sources, sceneRevision: 9u, frameIndex: 14);

            Assert.That(drawSet.MatchesPendingBuild(9u, 14), Is.True);
            drawSet.CompleteAndInvalidate();

            Assert.That(drawSet.HasPendingBuild, Is.False);
            Assert.That(drawSet.IsBuilt, Is.False);
            Assert.That(drawSet.MatchesPendingBuild(9u), Is.False);
            Assert.That(drawSet.LegacyInstanceIndexBuffer, Is.Null);
            Assert.That(drawSet.GetStats().UploadCount, Is.Zero);
            Assert.That(drawSet.GetStats().UploadBytes, Is.Zero);
            Assert.That(drawSet.FrameIndex, Is.EqualTo(-1));
            Assert.That(drawSet.SceneRevision, Is.Zero);
        }

        [Test]
        public void DrawSet_CompleteAndInvalidateMakesPublishedBuildUnmatchable()
        {
            using NativeArray<float4> planes = CreateBoxFrustumPlanes(5.0f);
            VividPrimitiveHandle handle = CreateHandle(0);
            using var records = new NativeArray<VividPrimitiveCullRecord>(1, Allocator.TempJob)
            {
                [0] = CreateRecord(
                    handle,
                    new float3(-1.0f),
                    new float3(1.0f),
                    drawSectionCount: 1u),
            };
            using var sources = new NativeArray<VividPrimitiveDrawSourceData>(1, Allocator.TempJob)
            {
                [0] = CreateSource(handle, 0, 71, VividRendererListID.Default),
            };
            using var drawSet = new VividPrimitiveDrawSet();
            drawSet.Build(planes, 1u, records, sources, sceneRevision: 10u, frameIndex: 15);

            Assert.That(drawSet.IsBuilt, Is.True);
            Assert.That(drawSet.SceneRevision, Is.EqualTo(10u));
            Assert.That(drawSet.FrameIndex, Is.EqualTo(15));
            Assert.That(drawSet.LegacyInstanceIndexBuffer, Is.Not.Null);

            drawSet.CompleteAndInvalidate();

            Assert.That(drawSet.IsBuilt, Is.False);
            Assert.That(drawSet.TryGetBucket(VividRendererListID.Default, out _), Is.False);
            Assert.That(drawSet.DrawCount, Is.Zero);
            Assert.That(drawSet.FrameIndex, Is.EqualTo(-1));
            Assert.That(drawSet.SceneRevision, Is.Zero);
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

        [Test]
        public void DrawSetSystem_InvalidatesAllPendingBuildsWithoutPublishingBeforeSceneMutation()
        {
            using var records = new NativeArray<VividPrimitiveCullRecord>(0, Allocator.TempJob);
            using var sources = new NativeArray<VividPrimitiveDrawSourceData>(0, Allocator.TempJob);
            using var system = new VividPrimitiveDrawSetSystem();
            GameObject firstCameraObject = null;
            GameObject secondCameraObject = null;
            try
            {
                firstCameraObject = new GameObject("Pending Primitive DrawSet Camera A");
                secondCameraObject = new GameObject("Pending Primitive DrawSet Camera B");
                Camera firstCamera = firstCameraObject.AddComponent<Camera>();
                Camera secondCamera = secondCameraObject.AddComponent<Camera>();

                VividPrimitiveDrawSet first = system.Schedule(
                    firstCamera,
                    records,
                    sources,
                    sceneRevision: 12u,
                    frameIndex: 30);
                VividPrimitiveDrawSet second = system.Schedule(
                    secondCamera,
                    records,
                    sources,
                    sceneRevision: 12u,
                    frameIndex: 30);

                Assert.That(first.HasPendingBuild, Is.True);
                Assert.That(second.HasPendingBuild, Is.True);
                Assert.That(system.CompleteAndInvalidateAllBuilds(), Is.EqualTo(2));
                Assert.That(first.HasPendingBuild, Is.False);
                Assert.That(second.HasPendingBuild, Is.False);
                Assert.That(first.IsBuilt, Is.False);
                Assert.That(second.IsBuilt, Is.False);
                Assert.That(first.MatchesPendingBuild(12u), Is.False);
                Assert.That(second.MatchesPendingBuild(12u), Is.False);
                Assert.That(first.GetStats().UploadCount, Is.Zero);
                Assert.That(second.GetStats().UploadCount, Is.Zero);
                Assert.That(system.CompleteAndInvalidateAllBuilds(), Is.Zero);
            }
            finally
            {
                if (firstCameraObject != null)
                    Object.DestroyImmediate(firstCameraObject);
                if (secondCameraObject != null)
                    Object.DestroyImmediate(secondCameraObject);
            }
        }

        [Test]
        public void DrawSetSystem_PurgeDestroyedCameraCompletesItsPendingBuild()
        {
            using var records = new NativeArray<VividPrimitiveCullRecord>(0, Allocator.TempJob);
            using var sources = new NativeArray<VividPrimitiveDrawSourceData>(0, Allocator.TempJob);
            using var system = new VividPrimitiveDrawSetSystem();
            var cameraObject = new GameObject("Destroyed Pending Primitive DrawSet Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            system.Schedule(camera, records, sources, sceneRevision: 1u, frameIndex: 1);

            Object.DestroyImmediate(cameraObject);

            Assert.DoesNotThrow(system.PurgeDestroyedCameras);
            Assert.That(system.TryGet(camera, out _), Is.False);
            Assert.That(system.CompleteAndInvalidateAllBuilds(), Is.Zero);
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
