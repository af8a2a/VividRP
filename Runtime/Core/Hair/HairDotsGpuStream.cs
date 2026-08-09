using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    /// <summary>
    /// Owns a persistent DOTS mesh and GPU-side previous-frame history. A
    /// simulation writes current HairGpuStrandSegment values, then records this
    /// stream before RTASBuildPass.
    /// </summary>
    public sealed class HairDotsGpuStream : IDisposable
    {
        private const string ExpandKernelName = "ExpandHairDots";
        private const string CopyKernelName = "CopyHairHistory";

        private static readonly int CurrentSegmentsId =
            Shader.PropertyToID("_HairCurrentSegments");
        private static readonly int PreviousSegmentsId =
            Shader.PropertyToID("_HairPreviousSegments");
        private static readonly int HistoryDestinationId =
            Shader.PropertyToID("_HairHistoryDestination");
        private static readonly int VertexBufferId =
            Shader.PropertyToID("_HairDotsVertexBuffer");
        private static readonly int SegmentCountId =
            Shader.PropertyToID("_HairSegmentCount");
        private static readonly int ResetHistoryId =
            Shader.PropertyToID("_HairResetHistory");
        private static readonly int RadiusScaleId =
            Shader.PropertyToID("_HairRadiusScale");
        private static readonly int RadiusCompensationId =
            Shader.PropertyToID("_HairRadiusCompensation");

        private readonly ComputeShader m_Shader;
        private readonly int m_ExpandKernel;
        private readonly int m_CopyKernel;
        private readonly bool m_OwnsMesh;
        private readonly Mesh m_Mesh;

        private GraphicsBuffer m_VertexBuffer;
        private GraphicsBuffer m_HistoryBuffer;
        private HairStrandHistoryState m_HistoryState;
        private int m_SegmentCount;
        private bool m_ResetRequested;
        private bool m_Disposed;

        public Mesh Mesh => m_Mesh;
        public int SegmentCount => m_SegmentCount;
        public HairHistoryResetReason LastResetReason { get; private set; }

        public HairDotsGpuStream(
            int segmentCount,
            Mesh target = null,
            ComputeShader shader = null)
        {
            m_Shader = shader
                ?? PipelineResourceManager
                    .Get<VividRPCoreResources>()
                    ?.HairDotsVertexUpdateCompute;
            if (m_Shader == null)
            {
                throw new InvalidOperationException(
                    "HairDotsVertexUpdate.compute is unavailable. "
                    + "Recollect pipeline resources or pass it explicitly.");
            }

            m_ExpandKernel = m_Shader.FindKernel(ExpandKernelName);
            m_CopyKernel = m_Shader.FindKernel(CopyKernelName);
            m_OwnsMesh = target == null;
            m_Mesh = target != null ? target : new Mesh();
            _ = EnsureTopology(segmentCount);
        }

        public static GraphicsBuffer CreateSimulationBuffer(
            int segmentCapacity,
            string name = "Hair GPU Simulation Segments")
        {
            if (segmentCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(segmentCapacity));
            }

            return new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                segmentCapacity,
                HairGpuStrandSegment.Stride)
            {
                name = name,
            };
        }

        public void RequestHistoryReset()
        {
            ThrowIfDisposed();
            m_ResetRequested = true;
        }

        public void RecordGpuUpdate(
            CommandBuffer commandBuffer,
            GraphicsBuffer currentSegments,
            int segmentCount,
            Bounds conservativeBounds,
            int frameIndex,
            int topologyVersion = 0,
            bool forceHistoryReset = false,
            float radiusScale = 1.0f)
        {
            ThrowIfDisposed();
            if (commandBuffer == null)
                throw new ArgumentNullException(nameof(commandBuffer));
            ValidateSimulationBuffer(currentSegments, segmentCount);
            ValidateBounds(conservativeBounds);
            if (!IsFinite(radiusScale) || radiusScale <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusScale));
            }

            bool storageRecreated = EnsureTopology(segmentCount);
            bool resetRequested = forceHistoryReset || m_ResetRequested;
            LastResetReason = m_HistoryState.CommitFrame(
                frameIndex,
                segmentCount,
                topologyVersion,
                resetRequested,
                storageRecreated);
            m_ResetRequested = false;
            m_Mesh.bounds = conservativeBounds;

            commandBuffer.SetComputeIntParam(
                m_Shader,
                SegmentCountId,
                segmentCount);
            commandBuffer.SetComputeIntParam(
                m_Shader,
                ResetHistoryId,
                LastResetReason != HairHistoryResetReason.None ? 1 : 0);
            commandBuffer.SetComputeFloatParam(
                m_Shader,
                RadiusScaleId,
                radiusScale);
            commandBuffer.SetComputeFloatParam(
                m_Shader,
                RadiusCompensationId,
                HairDotsMeshBuilder.RadiusCompensation);
            commandBuffer.SetComputeBufferParam(
                m_Shader,
                m_ExpandKernel,
                CurrentSegmentsId,
                currentSegments);
            commandBuffer.SetComputeBufferParam(
                m_Shader,
                m_ExpandKernel,
                PreviousSegmentsId,
                m_HistoryBuffer);
            commandBuffer.SetComputeBufferParam(
                m_Shader,
                m_ExpandKernel,
                VertexBufferId,
                m_VertexBuffer);

            int threadGroups = (segmentCount + 63) / 64;
            commandBuffer.DispatchCompute(
                m_Shader,
                m_ExpandKernel,
                threadGroups,
                1,
                1);

            commandBuffer.SetComputeBufferParam(
                m_Shader,
                m_CopyKernel,
                CurrentSegmentsId,
                currentSegments);
            commandBuffer.SetComputeBufferParam(
                m_Shader,
                m_CopyKernel,
                HistoryDestinationId,
                m_HistoryBuffer);
            commandBuffer.DispatchCompute(
                m_Shader,
                m_CopyKernel,
                threadGroups,
                1,
                1);
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            m_VertexBuffer?.Dispose();
            m_HistoryBuffer?.Dispose();
            if (m_OwnsMesh)
                CoreUtils.Destroy(m_Mesh);
            m_Disposed = true;
        }

        private bool EnsureTopology(int segmentCount)
        {
            if (segmentCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(segmentCount));
            if (segmentCount == m_SegmentCount
                && m_VertexBuffer != null
                && m_VertexBuffer.IsValid()
                && m_HistoryBuffer != null
                && m_HistoryBuffer.IsValid())
            {
                return false;
            }

            m_VertexBuffer?.Dispose();
            m_HistoryBuffer?.Dispose();
            HairDotsMeshBuilder.CreatePersistent(segmentCount, m_Mesh);
            int stride = m_Mesh.GetVertexBufferStride(0);
            if (stride != HairDotsMeshBuilder.PersistentVertexStride)
            {
                throw new InvalidOperationException(
                    $"Unexpected Hair vertex stride {stride}; expected "
                    + $"{HairDotsMeshBuilder.PersistentVertexStride}.");
            }

            m_VertexBuffer = m_Mesh.GetVertexBuffer(0);
            m_HistoryBuffer = CreateSimulationBuffer(
                segmentCount,
                "Hair Previous Segment History");
            m_SegmentCount = segmentCount;
            return true;
        }

        private static void ValidateSimulationBuffer(
            GraphicsBuffer buffer,
            int segmentCount)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (!buffer.IsValid())
                throw new ArgumentException(
                    "The simulation buffer is not valid.",
                    nameof(buffer));
            if (segmentCount <= 0 || segmentCount > buffer.count)
                throw new ArgumentOutOfRangeException(nameof(segmentCount));
            if (buffer.stride != HairGpuStrandSegment.Stride)
            {
                throw new ArgumentException(
                    $"Hair simulation buffer stride must be "
                    + $"{HairGpuStrandSegment.Stride} bytes.",
                    nameof(buffer));
            }
        }

        private static void ValidateBounds(Bounds bounds)
        {
            if (!IsFinite(bounds.center)
                || !IsFinite(bounds.extents)
                || bounds.extents.x < 0.0f
                || bounds.extents.y < 0.0f
                || bounds.extents.z < 0.0f)
            {
                throw new ArgumentException(
                    "Hair bounds must be finite and non-negative.",
                    nameof(bounds));
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(HairDotsGpuStream));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                   && IsFinite(value.y)
                   && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
