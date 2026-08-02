using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime
{
    [Serializable]
    public struct HairStrandPoint
    {
        public Vector3 Position;
        public float Radius;
        public Vector2 UV;

        public HairStrandPoint(Vector3 position, float radius, Vector2 uv)
        {
            Position = position;
            Radius = radius;
            UV = uv;
        }
    }

    [Serializable]
    public struct HairStrandSegment
    {
        public HairStrandPoint Start;
        public HairStrandPoint End;

        public HairStrandSegment(HairStrandPoint start, HairStrandPoint end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// GPU simulation contract for one line segment. The layout is three
    /// float4 values and must remain synchronized with HairDotsVertexUpdate.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HairGpuStrandSegment
    {
        public const int Stride = 48;

        public Vector4 StartPositionRadius;
        public Vector4 EndPositionRadius;
        public Vector4 StartEndUV;

        public HairGpuStrandSegment(HairStrandSegment segment)
        {
            StartPositionRadius = new Vector4(
                segment.Start.Position.x,
                segment.Start.Position.y,
                segment.Start.Position.z,
                segment.Start.Radius);
            EndPositionRadius = new Vector4(
                segment.End.Position.x,
                segment.End.Position.y,
                segment.End.Position.z,
                segment.End.Radius);
            StartEndUV = new Vector4(
                segment.Start.UV.x,
                segment.Start.UV.y,
                segment.End.UV.x,
                segment.End.UV.y);
        }
    }

    public enum HairHistoryResetReason
    {
        None,
        FirstFrame,
        Explicit,
        TopologyChanged,
        StorageRecreated,
        FrameDiscontinuity,
    }

    /// <summary>
    /// Tracks whether strand history is safe to reuse for a frame.
    /// </summary>
    public struct HairStrandHistoryState
    {
        private bool m_IsValid;
        private int m_LastFrameIndex;
        private int m_LastSegmentCount;
        private int m_LastTopologyVersion;

        public bool IsValid => m_IsValid;

        public HairHistoryResetReason CommitFrame(
            int frameIndex,
            int segmentCount,
            int topologyVersion,
            bool forceReset = false,
            bool storageRecreated = false)
        {
            if (frameIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));
            if (segmentCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(segmentCount));

            HairHistoryResetReason reason;
            if (forceReset)
            {
                reason = HairHistoryResetReason.Explicit;
            }
            else if (!m_IsValid)
            {
                reason = HairHistoryResetReason.FirstFrame;
            }
            else if (segmentCount != m_LastSegmentCount
                     || topologyVersion != m_LastTopologyVersion)
            {
                reason = HairHistoryResetReason.TopologyChanged;
            }
            else if (storageRecreated)
            {
                reason = HairHistoryResetReason.StorageRecreated;
            }
            else if (frameIndex != m_LastFrameIndex + 1)
            {
                reason = HairHistoryResetReason.FrameDiscontinuity;
            }
            else
            {
                reason = HairHistoryResetReason.None;
            }

            m_IsValid = true;
            m_LastFrameIndex = frameIndex;
            m_LastSegmentCount = segmentCount;
            m_LastTopologyVersion = topologyVersion;
            return reason;
        }

        public void Invalidate()
        {
            m_IsValid = false;
        }
    }
}
