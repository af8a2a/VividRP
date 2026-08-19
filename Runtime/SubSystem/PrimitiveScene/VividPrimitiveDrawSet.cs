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
        private const int CullJobBatchSize = 64;
        private static readonly ProfilerMarker s_BuildMarker = new("VividRP.PrimitiveScene.DrawSet.Build");
        private static readonly ProfilerMarker s_CullMarker = new("VividRP.PrimitiveScene.DrawSet.Cull");
        private static readonly ProfilerMarker s_BucketMarker = new("VividRP.PrimitiveScene.DrawSet.Bucket");
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

        internal GraphicsBuffer LegacyInstanceIndexBuffer => m_LegacyInstanceIndexBuffer;

        internal NativeArray<VividPrimitiveDrawSetEntry> Entries =>
            m_Entries.IsCreated ? m_Entries.GetSubArray(0, m_DrawCount) : default;

        internal NativeArray<uint> LegacyInstanceIndices =>
            m_LegacyInstanceIndices.IsCreated
                ? m_LegacyInstanceIndices.GetSubArray(0, m_DrawCount)
                : default;

        internal NativeArray<VividPrimitiveDrawBucket> Buckets =>
            m_Buckets.IsCreated ? m_Buckets : default;

        internal void Build(
            Camera camera,
            NativeArray<VividPrimitiveCullRecord> cullingRecords,
            NativeArray<VividPrimitiveDrawSourceData> drawSources,
            uint sceneRevision,
            int frameIndex)
        {
            ThrowIfDisposed();
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            CompletePendingBuild();
            // Match the projection used by VividGPUDrivenCullingContextUtility so the
            // CPU coarse test cannot reject geometry accepted by the jittered GPU view.
            Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            GeometryUtility.CalculateFrustumPlanes(viewProjection, m_PlaneScratch);
            EnsureFixedNativeResources();
            for (int planeIndex = 0; planeIndex < FrustumPlaneCount; planeIndex++)
            {
                Plane plane = m_PlaneScratch[planeIndex];
                Vector3 normal = plane.normal;
                m_FrustumPlanes[planeIndex] = new float4(
                    normal.x,
                    normal.y,
                    normal.z,
                    plane.distance);
            }

            Build(
                m_FrustumPlanes,
                unchecked((uint) camera.cullingMask),
                cullingRecords,
                drawSources,
                sceneRevision,
                frameIndex);
        }

        internal void Build(
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
            if (!cullingRecords.IsCreated)
                throw new ArgumentException("Culling records must be a created NativeArray.", nameof(cullingRecords));
            if (!drawSources.IsCreated)
                throw new ArgumentException("Draw sources must be a created NativeArray.", nameof(drawSources));

            using (s_BuildMarker.Auto())
            {
                CompletePendingBuild();
                EnsureCapacity(cullingRecords.Length, drawSources.Length);
                CopyFrustumPlanes(frustumPlanes);
                m_InputPrimitiveCount = cullingRecords.Length;
                m_InputDrawSourceCount = drawSources.Length;
                m_FrameIndex = frameIndex;
                m_SceneRevision = sceneRevision;
                m_IsBuilt = false;

                JobHandle cullHandle = default;
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
                        }.Schedule(cullingRecords.Length, CullJobBatchSize);
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
                    }.Schedule(cullHandle);
                    m_HasPendingBuild = true;
                    JobHandle.ScheduleBatchedJobs();
                }

                CompletePendingBuild();
                VividPrimitiveDrawSetBuildResult result = m_BuildResult[0];
                m_VisiblePrimitiveCount = result.VisiblePrimitiveCount;
                m_DrawCount = result.DrawCount;
                m_NonEmptyBucketCount = result.NonEmptyBucketCount;
                UploadLegacyInstanceIndices();
                m_IsBuilt = true;
            }
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

        private void EnsureCapacity(int primitiveCount, int drawSourceCount)
        {
            EnsureFixedNativeResources();
            EnsureNativeCapacity(ref m_Visibility, primitiveCount);
            EnsureNativeCapacity(ref m_Entries, drawSourceCount);
            EnsureNativeCapacity(ref m_LegacyInstanceIndices, drawSourceCount);
        }

        private void EnsureFixedNativeResources()
        {
            if (!m_FrustumPlanes.IsCreated)
            {
                m_FrustumPlanes = new NativeArray<float4>(
                    FrustumPlaneCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }
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

        private static void EnsureNativeCapacity<T>(ref NativeArray<T> array, int requiredCount)
            where T : struct
        {
            requiredCount = Mathf.Max(1, requiredCount);
            if (array.IsCreated && array.Length >= requiredCount)
                return;

            int capacity = Mathf.NextPowerOfTwo(requiredCount);
            DisposeIfCreated(ref array);
            array = new NativeArray<T>(
                capacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
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
    }

    internal sealed class VividPrimitiveDrawSetSystem : CameraRelativeSystem<VividPrimitiveDrawSet>
    {
        internal VividPrimitiveDrawSet Build(
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
            drawSet.Build(
                camera,
                cullingRecords,
                drawSources,
                sceneRevision,
                frameIndex >= 0 ? frameIndex : Time.frameCount);
            return drawSet;
        }

        internal bool TryGet(Camera camera, out VividPrimitiveDrawSet drawSet)
        {
            return TryGetBase(camera, out drawSet) && drawSet?.IsBuilt == true;
        }
    }
}
