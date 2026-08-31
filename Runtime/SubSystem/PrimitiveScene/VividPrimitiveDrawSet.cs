using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.PrimitiveScene
{
    internal sealed class VividPrimitiveDrawSet : CameraRelativeState
    {
        private const int FrustumPlaneCount = 6;
        private const int NearFrustumPlaneIndex = 4;
        private const int CullJobBatchSize = 64;
        private const float FrustumMarginPixels = 4.0f;
        private static readonly ProfilerMarker s_BuildMarker = new("VividRP.PrimitiveScene.DrawSet.Build");
        private static readonly ProfilerMarker s_CullMarker = new("VividRP.PrimitiveScene.DrawSet.Cull");
        private static readonly ProfilerMarker s_BucketMarker = new("VividRP.PrimitiveScene.DrawSet.Bucket");
        private static readonly ProfilerMarker s_CompleteMarker = new("VividRP.PrimitiveScene.DrawSet.Complete");
        private static readonly ProfilerMarker s_UploadMarker = new("VividRP.PrimitiveScene.DrawSet.Upload");

        private readonly Plane[] m_PlaneScratch = new Plane[FrustumPlaneCount];

        private NativeArray<float4> m_FrustumPlanes;
        private NativeArray<byte> m_Visibility;
        private NativeArray<VividPrimitiveDrawSetEntry> m_Entries;
        private NativeArray<uint> m_LegacyInstanceIndices;
        private NativeArray<VividPrimitiveDrawBucket> m_Buckets;
        private NativeArray<int> m_BucketCounts;
        private NativeArray<int> m_BucketWriteCursors;
        private NativeArray<VividPrimitiveDrawSetBuildResult> m_BuildResult;
        private GraphicsBuffer m_LegacyInstanceIndexBuffer;
        private JobHandle m_PendingBuild;
        private bool m_HasPendingBuild;
        private bool m_IsBuilt;
        private bool m_IsDisposed;
        private int m_InputPrimitiveCount;
        private int m_InputDrawSourceCount;
        private int m_VisiblePrimitiveCount;
        private int m_DrawCount;
        private int m_NonEmptyBucketCount;
        private int m_UploadCount;
        private long m_UploadBytes;
        private int m_FrameIndex = -1;
        private uint m_SceneRevision;

        internal int DrawCount => m_DrawCount;

        internal int VisiblePrimitiveCount => m_VisiblePrimitiveCount;

        internal int NonEmptyBucketCount => m_NonEmptyBucketCount;

        internal int FrameIndex => m_FrameIndex;

        internal uint SceneRevision => m_SceneRevision;

        internal bool IsBuilt => m_IsBuilt && !m_IsDisposed;

        internal bool HasPendingBuild => m_HasPendingBuild && !m_IsDisposed;

        internal GraphicsBuffer LegacyInstanceIndexBuffer => m_LegacyInstanceIndexBuffer;

        internal NativeArray<VividPrimitiveDrawSetEntry> Entries =>
            m_Entries.IsCreated ? m_Entries.GetSubArray(0, m_DrawCount) : default;

        internal NativeArray<uint> LegacyInstanceIndices =>
            m_LegacyInstanceIndices.IsCreated
                ? m_LegacyInstanceIndices.GetSubArray(0, m_DrawCount)
                : default;

        internal NativeArray<VividPrimitiveDrawBucket> Buckets =>
            m_Buckets.IsCreated ? m_Buckets : default;

        internal bool MatchesPendingBuild(uint sceneRevision, int frameIndex)
        {
            return MatchesPendingBuild(sceneRevision) && m_FrameIndex == frameIndex;
        }

        internal bool MatchesPendingBuild(uint sceneRevision)
        {
            return HasPendingBuild && m_SceneRevision == sceneRevision;
        }

        internal void Schedule(
            Camera camera,
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources,
            uint sceneRevision,
            int frameIndex)
        {
            ThrowIfDisposed();
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));
            ValidateInputs(cullingRecords, drawSources);

            // beginCameraRendering precedes temporal jitter, so use an expanded form
            // of the same non-jittered projection VividRP will use as its jitter base.
            Matrix4x4 projection = ExpandProjectionForCoarseCulling(
                CameraProjectionMatrixUtility.GetNonJitteredProjectionMatrix(camera),
                camera.pixelWidth,
                camera.pixelHeight);
            Matrix4x4 viewProjection = projection * camera.worldToCameraMatrix;
            GeometryUtility.CalculateFrustumPlanes(viewProjection, m_PlaneScratch);

            using (s_BuildMarker.Auto())
            {
                JobHandle dependency = PrepareSchedule(cullingRecords.Length, drawSources.Length, 1);
                CopyFrustumPlanes(m_PlaneScratch);
                ScheduleJobs(
                    unchecked((uint) camera.cullingMask),
                    VividInstancePassMask.Main,
                    1,
                    cullingRecords,
                    drawSources,
                    sceneRevision,
                    frameIndex,
                    dependency);
            }
        }

        internal void Schedule(
            NativeArray<float4> frustumPlanes,
            uint cameraCullingMask,
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources,
            uint sceneRevision,
            int frameIndex)
        {
            ThrowIfDisposed();
            if (!frustumPlanes.IsCreated || frustumPlanes.Length < FrustumPlaneCount)
                throw new ArgumentException("DrawSet requires six frustum planes.", nameof(frustumPlanes));
            ValidateInputs(cullingRecords, drawSources);

            using (s_BuildMarker.Auto())
            {
                JobHandle dependency = PrepareSchedule(cullingRecords.Length, drawSources.Length, 1);
                CopyFrustumPlanes(frustumPlanes);
                ScheduleJobs(
                    cameraCullingMask,
                    VividInstancePassMask.Main,
                    1,
                    cullingRecords,
                    drawSources,
                    sceneRevision,
                    frameIndex,
                    dependency);
            }
        }

        internal void Schedule(
            Camera camera,
            Matrix4x4[] viewMatrices,
            Matrix4x4[] projectionMatrices,
            int frustumCount,
            VividInstancePassMask requiredPassMask,
            bool cullAgainstNearPlane,
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources,
            uint sceneRevision,
            int frameIndex)
        {
            ThrowIfDisposed();
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));
            if (viewMatrices == null)
                throw new ArgumentNullException(nameof(viewMatrices));
            if (projectionMatrices == null)
                throw new ArgumentNullException(nameof(projectionMatrices));
            if (frustumCount <= 0
                || frustumCount > viewMatrices.Length
                || frustumCount > projectionMatrices.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(frustumCount));
            }
            if (requiredPassMask == 0)
                throw new ArgumentOutOfRangeException(nameof(requiredPassMask));
            ValidateInputs(cullingRecords, drawSources);

            using (s_BuildMarker.Auto())
            {
                JobHandle dependency = PrepareSchedule(
                    cullingRecords.Length,
                    drawSources.Length,
                    frustumCount);
                CopyFrustumPlanes(
                    viewMatrices,
                    projectionMatrices,
                    frustumCount,
                    cullAgainstNearPlane);
                ScheduleJobs(
                    unchecked((uint) camera.cullingMask),
                    requiredPassMask,
                    frustumCount,
                    cullingRecords,
                    drawSources,
                    sceneRevision,
                    frameIndex,
                    dependency);
            }
        }

        internal void Build(
            Camera camera,
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources,
            uint sceneRevision,
            int frameIndex)
        {
            Schedule(camera, cullingRecords, drawSources, sceneRevision, frameIndex);
            CompleteScheduledBuild();
        }

        internal void Build(
            NativeArray<float4> frustumPlanes,
            uint cameraCullingMask,
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources,
            uint sceneRevision,
            int frameIndex)
        {
            Schedule(
                frustumPlanes,
                cameraCullingMask,
                cullingRecords,
                drawSources,
                sceneRevision,
                frameIndex);
            CompleteScheduledBuild();
        }

        internal bool CompleteScheduledBuild()
        {
            ThrowIfDisposed();
            if (!m_HasPendingBuild)
                return m_IsBuilt;

            using (s_CompleteMarker.Auto())
            {
                CompletePendingBuild();
                VividPrimitiveDrawSetBuildResult result = m_BuildResult[0];
                m_VisiblePrimitiveCount = result.VisiblePrimitiveCount;
                m_DrawCount = result.DrawCount;
                m_NonEmptyBucketCount = result.NonEmptyBucketCount;
                UploadLegacyInstanceIndices();
                m_IsBuilt = true;
                return true;
            }
        }

        internal void CompleteAndInvalidate()
        {
            ThrowIfDisposed();
            // This is a write barrier for PrimitiveScene's NativeLists. Wait for any
            // readers, but deliberately skip result publication and GPU upload because
            // the source snapshot is about to change.
            CompletePendingBuild();
            m_IsBuilt = false;
            m_InputPrimitiveCount = 0;
            m_InputDrawSourceCount = 0;
            m_VisiblePrimitiveCount = 0;
            m_DrawCount = 0;
            m_NonEmptyBucketCount = 0;
            m_UploadCount = 0;
            m_UploadBytes = 0L;
            m_FrameIndex = -1;
            m_SceneRevision = 0u;
        }

        internal bool TryGetBucket(
            VividRendererListID rendererListID,
            out VividPrimitiveDrawBucket bucket)
        {
            bucket = default;
            int bucketIndex = (int) rendererListID;
            if (!IsBuilt
                || !m_Buckets.IsCreated
                || (uint) bucketIndex >= VividPrimitiveBuildDrawSetJob.RendererListCount)
            {
                return false;
            }

            bucket = m_Buckets[bucketIndex];
            return bucket.DrawCount > 0u;
        }

        internal VividPrimitiveDrawSetStats GetStats()
        {
            return new VividPrimitiveDrawSetStats(
                m_InputPrimitiveCount,
                m_InputDrawSourceCount,
                m_VisiblePrimitiveCount,
                m_DrawCount,
                m_NonEmptyBucketCount,
                m_Visibility.IsCreated ? m_Visibility.Length : 0,
                m_Entries.IsCreated ? m_Entries.Length : 0,
                m_LegacyInstanceIndexBuffer?.count ?? 0,
                m_UploadCount,
                m_UploadBytes,
                m_FrameIndex,
                m_SceneRevision);
        }

        public override void Dispose()
        {
            if (m_IsDisposed)
                return;

            CompletePendingBuild();
            DisposeIfCreated(ref m_FrustumPlanes);
            DisposeIfCreated(ref m_Visibility);
            DisposeIfCreated(ref m_Entries);
            DisposeIfCreated(ref m_LegacyInstanceIndices);
            DisposeIfCreated(ref m_Buckets);
            DisposeIfCreated(ref m_BucketCounts);
            DisposeIfCreated(ref m_BucketWriteCursors);
            DisposeIfCreated(ref m_BuildResult);
            m_LegacyInstanceIndexBuffer?.Dispose();
            m_LegacyInstanceIndexBuffer = null;
            m_IsBuilt = false;
            m_IsDisposed = true;
        }

        private JobHandle PrepareSchedule(int primitiveCount, int drawSourceCount, int frustumCount)
        {
            EnsureFixedNativeResources();
            JobHandle dependency = m_HasPendingBuild ? m_PendingBuild : default;
            bool deferDisposal = m_HasPendingBuild;
            JobHandle deferredDisposals = default;
            bool hasDeferredDisposals = false;

            // Plane coefficients are populated on the CPU. Allocate fresh storage while
            // an earlier cull still reads the previous planes, then retire it after the
            // earlier build completes.
            int requiredPlaneCount = frustumCount * FrustumPlaneCount;
            if (deferDisposal)
            {
                int planeCapacity = Mathf.NextPowerOfTwo(
                    Mathf.Max(requiredPlaneCount, m_FrustumPlanes.IsCreated ? m_FrustumPlanes.Length : 1));
                ScheduleDeferredDispose(
                    ref m_FrustumPlanes,
                    dependency,
                    ref deferredDisposals,
                    ref hasDeferredDisposals);
                m_FrustumPlanes = CreateNativeArray<float4>(planeCapacity);
            }
            else
            {
                EnsureNativeCapacity(
                    ref m_FrustumPlanes,
                    requiredPlaneCount,
                    dependency,
                    false,
                    ref deferredDisposals,
                    ref hasDeferredDisposals);
            }

            EnsureNativeCapacity(
                ref m_Visibility,
                primitiveCount,
                dependency,
                deferDisposal,
                ref deferredDisposals,
                ref hasDeferredDisposals);
            EnsureNativeCapacity(
                ref m_Entries,
                drawSourceCount,
                dependency,
                deferDisposal,
                ref deferredDisposals,
                ref hasDeferredDisposals);
            EnsureNativeCapacity(
                ref m_LegacyInstanceIndices,
                drawSourceCount,
                dependency,
                deferDisposal,
                ref deferredDisposals,
                ref hasDeferredDisposals);

            JobHandle preparedDependency = hasDeferredDisposals
                ? JobHandle.CombineDependencies(dependency, deferredDisposals)
                : dependency;
            if (m_HasPendingBuild)
                m_PendingBuild = preparedDependency;
            return preparedDependency;
        }

        private void EnsureFixedNativeResources()
        {
            if (!m_Buckets.IsCreated)
            {
                m_Buckets = new NativeArray<VividPrimitiveDrawBucket>(
                    VividPrimitiveBuildDrawSetJob.RendererListCount,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
            }
            if (!m_BucketCounts.IsCreated)
            {
                m_BucketCounts = new NativeArray<int>(
                    VividPrimitiveBuildDrawSetJob.RendererListCount,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
            }
            if (!m_BucketWriteCursors.IsCreated)
            {
                m_BucketWriteCursors = new NativeArray<int>(
                    VividPrimitiveBuildDrawSetJob.RendererListCount,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
            }
            if (!m_BuildResult.IsCreated)
            {
                m_BuildResult = new NativeArray<VividPrimitiveDrawSetBuildResult>(
                    1,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
            }
        }

        private void CopyFrustumPlanes(NativeArray<float4> source)
        {
            for (int planeIndex = 0; planeIndex < FrustumPlaneCount; planeIndex++)
                m_FrustumPlanes[planeIndex] = source[planeIndex];
        }

        private void CopyFrustumPlanes(Plane[] source)
        {
            for (int planeIndex = 0; planeIndex < FrustumPlaneCount; planeIndex++)
            {
                Plane plane = source[planeIndex];
                Vector3 normal = plane.normal;
                m_FrustumPlanes[planeIndex] = new float4(
                    normal.x,
                    normal.y,
                    normal.z,
                    plane.distance);
            }
        }

        private void CopyFrustumPlanes(
            Matrix4x4[] viewMatrices,
            Matrix4x4[] projectionMatrices,
            int frustumCount,
            bool cullAgainstNearPlane)
        {
            for (int frustumIndex = 0; frustumIndex < frustumCount; frustumIndex++)
            {
                GeometryUtility.CalculateFrustumPlanes(
                    projectionMatrices[frustumIndex] * viewMatrices[frustumIndex],
                    m_PlaneScratch);
                int planeOffset = frustumIndex * FrustumPlaneCount;
                for (int planeIndex = 0; planeIndex < FrustumPlaneCount; planeIndex++)
                {
                    Plane plane = m_PlaneScratch[planeIndex];
                    Vector3 normal = plane.normal;
                    bool disablePlane = !cullAgainstNearPlane
                                        && planeIndex == NearFrustumPlaneIndex;
                    m_FrustumPlanes[planeOffset + planeIndex] = disablePlane
                        ? float4.zero
                        : new float4(normal.x, normal.y, normal.z, plane.distance);
                }
            }
        }

        internal static Matrix4x4 ExpandProjectionForCoarseCulling(
            Matrix4x4 projection,
            int pixelWidth,
            int pixelHeight)
        {
            // beginCameraRendering fires before VividRP applies temporal jitter. Expand
            // the unjittered frustum by a small screen-space guard band so a later TAA,
            // TSR, FSR or DLSS offset cannot turn this coarse CPU reject into a false
            // negative. GPU culling still uses the exact jittered projection.
            float horizontalScale = 1.0f
                                    / (1.0f + (2.0f * FrustumMarginPixels)
                                        / Mathf.Max(1, pixelWidth));
            float verticalScale = 1.0f
                                  / (1.0f + (2.0f * FrustumMarginPixels)
                                      / Mathf.Max(1, pixelHeight));
            for (int column = 0; column < 4; column++)
            {
                projection[0, column] *= horizontalScale;
                projection[1, column] *= verticalScale;
            }

            return projection;
        }

        private void ScheduleJobs(
            uint cameraCullingMask,
            VividInstancePassMask requiredPassMask,
            int frustumCount,
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources,
            uint sceneRevision,
            int frameIndex,
            JobHandle dependency)
        {
            m_InputPrimitiveCount = cullingRecords.Length;
            m_InputDrawSourceCount = drawSources.Length;
            m_VisiblePrimitiveCount = 0;
            m_DrawCount = 0;
            m_NonEmptyBucketCount = 0;
            m_UploadCount = 0;
            m_UploadBytes = 0L;
            m_FrameIndex = frameIndex;
            m_SceneRevision = sceneRevision;
            m_IsBuilt = false;

            JobHandle cullHandle = dependency;
            if (cullingRecords.Length > 0)
            {
                using (s_CullMarker.Auto())
                {
                    cullHandle = new VividPrimitiveFrustumCullJob
                    {
                        CullingRecords = cullingRecords,
                        FrustumPlanes = m_FrustumPlanes,
                        Visibility = m_Visibility.GetSubArray(0, cullingRecords.Length),
                        CameraCullingMask = cameraCullingMask,
                        RequiredPassMask = requiredPassMask,
                        FrustumCount = frustumCount,
                    }.Schedule(cullingRecords.Length, CullJobBatchSize, dependency);
                }
            }

            using (s_BucketMarker.Auto())
            {
                m_PendingBuild = new VividPrimitiveBuildDrawSetJob
                {
                    CullingRecords = cullingRecords,
                    Visibility = m_Visibility.GetSubArray(0, cullingRecords.Length),
                    DrawSources = drawSources,
                    Entries = m_Entries,
                    LegacyInstanceIndices = m_LegacyInstanceIndices,
                    Buckets = m_Buckets,
                    BucketCounts = m_BucketCounts,
                    BucketWriteCursors = m_BucketWriteCursors,
                    Result = m_BuildResult,
                    RequiredPassMask = requiredPassMask,
                }.Schedule(cullHandle);
                m_HasPendingBuild = true;
                JobHandle.ScheduleBatchedJobs();
            }
        }

        private void CompletePendingBuild()
        {
            if (!m_HasPendingBuild)
                return;

            m_PendingBuild.Complete();
            m_PendingBuild = default;
            m_HasPendingBuild = false;
        }

        private void UploadLegacyInstanceIndices()
        {
            using (s_UploadMarker.Auto())
            {
                int requiredCount = Mathf.Max(1, m_DrawCount);
                if (m_LegacyInstanceIndexBuffer == null
                    || m_LegacyInstanceIndexBuffer.target != GraphicsBuffer.Target.Structured
                    || m_LegacyInstanceIndexBuffer.stride != sizeof(uint)
                    || m_LegacyInstanceIndexBuffer.count < requiredCount)
                {
                    int capacity = Mathf.NextPowerOfTwo(requiredCount);
                    m_LegacyInstanceIndexBuffer?.Dispose();
                    m_LegacyInstanceIndexBuffer = new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured,
                        capacity,
                        sizeof(uint))
                    {
                        name = "VividPrimitiveDrawSet_LegacyInstanceIndices",
                    };
                }

                m_UploadCount = 0;
                m_UploadBytes = 0L;
                if (m_DrawCount <= 0)
                    return;

                m_LegacyInstanceIndexBuffer.SetData(
                    m_LegacyInstanceIndices,
                    0,
                    0,
                    m_DrawCount);
                m_UploadCount = 1;
                m_UploadBytes = (long) m_DrawCount * sizeof(uint);
            }
        }

        private static void EnsureNativeCapacity<T>(
            ref NativeArray<T> array,
            int requiredCount,
            JobHandle dependency,
            bool deferDisposal,
            ref JobHandle deferredDisposals,
            ref bool hasDeferredDisposals)
            where T : struct
        {
            requiredCount = Mathf.Max(1, requiredCount);
            if (array.IsCreated && array.Length >= requiredCount)
                return;

            int capacity = Mathf.NextPowerOfTwo(requiredCount);
            if (deferDisposal)
                ScheduleDeferredDispose(ref array, dependency, ref deferredDisposals, ref hasDeferredDisposals);
            else
                DisposeIfCreated(ref array);
            array = CreateNativeArray<T>(capacity);
        }

        private static NativeArray<T> CreateNativeArray<T>(int length)
            where T : struct
        {
            return new NativeArray<T>(
                length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private static void ScheduleDeferredDispose<T>(
            ref NativeArray<T> array,
            JobHandle dependency,
            ref JobHandle deferredDisposals,
            ref bool hasDeferredDisposals)
            where T : struct
        {
            if (!array.IsCreated)
                return;

            JobHandle disposeHandle = array.Dispose(dependency);
            array = default;
            deferredDisposals = hasDeferredDisposals
                ? JobHandle.CombineDependencies(deferredDisposals, disposeHandle)
                : disposeHandle;
            hasDeferredDisposals = true;
        }

        private static void DisposeIfCreated<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
                array.Dispose();
            array = default;
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
                throw new ObjectDisposedException(nameof(VividPrimitiveDrawSet));
        }

        private static void ValidateInputs(
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources)
        {
            if (!cullingRecords.IsCreated)
                throw new ArgumentException("Culling records must be a created NativeArray.", nameof(cullingRecords));
            if (!drawSources.IsCreated)
                throw new ArgumentException("Draw sources must be a created NativeArray.", nameof(drawSources));
        }
    }

    internal sealed class VividPrimitiveDrawSetSystem : CameraRelativeSystem<VividPrimitiveDrawSet>
    {
        internal VividPrimitiveDrawSet Schedule(
            Camera camera,
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources,
            uint sceneRevision,
            int frameIndex = -1)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            PurgeDestroyedCameras();
            VividPrimitiveDrawSet drawSet = GetOrCreateBase(camera);
            drawSet.Schedule(
                camera,
                cullingRecords,
                drawSources,
                sceneRevision,
                frameIndex >= 0 ? frameIndex : Time.frameCount);
            return drawSet;
        }

        internal VividPrimitiveDrawSet Build(
            Camera camera,
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources,
            uint sceneRevision,
            int frameIndex = -1)
        {
            VividPrimitiveDrawSet drawSet = Schedule(
                camera,
                cullingRecords,
                drawSources,
                sceneRevision,
                frameIndex);
            drawSet.CompleteScheduledBuild();
            return drawSet;
        }

        internal VividPrimitiveDrawSet Schedule(
            Camera camera,
            Matrix4x4[] viewMatrices,
            Matrix4x4[] projectionMatrices,
            int frustumCount,
            VividInstancePassMask requiredPassMask,
            bool cullAgainstNearPlane,
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources,
            uint sceneRevision,
            int frameIndex = -1)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            PurgeDestroyedCameras();
            VividPrimitiveDrawSet drawSet = GetOrCreateBase(camera);
            drawSet.Schedule(
                camera,
                viewMatrices,
                projectionMatrices,
                frustumCount,
                requiredPassMask,
                cullAgainstNearPlane,
                cullingRecords,
                drawSources,
                sceneRevision,
                frameIndex >= 0 ? frameIndex : Time.frameCount);
            return drawSet;
        }

        internal int CompleteAndInvalidateAllBuilds()
        {
            int invalidatedCount = 0;
            foreach (VividPrimitiveDrawSet drawSet in m_CameraStates.Values)
            {
                if (drawSet == null || (!drawSet.HasPendingBuild && !drawSet.IsBuilt))
                    continue;

                drawSet.CompleteAndInvalidate();
                invalidatedCount++;
            }

            return invalidatedCount;
        }

        internal bool TryGet(Camera camera, out VividPrimitiveDrawSet drawSet)
        {
            return TryGetBase(camera, out drawSet) && drawSet?.IsBuilt == true;
        }
    }
}
