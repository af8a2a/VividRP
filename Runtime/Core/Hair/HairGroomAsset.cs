using System;
using UnityEngine;

namespace VividRP.Runtime
{
    /// <summary>
    /// GPU buffers owned by a loaded HairGroomAsset, shared across realtime
    /// Strand Visibility Buffer passes and DXR/DOTS path-tracing conversion.
    /// </summary>
    public sealed class HairGroomGpuResource : IDisposable
    {
        public GraphicsBuffer StrandBuffer { get; private set; }
        public GraphicsBuffer RestVertexBuffer { get; private set; }
        public GraphicsBuffer SegmentIndexBuffer { get; private set; }
        public GraphicsBuffer VisibilitySHBuffer { get; private set; }
        public int StrandCount { get; private set; }
        public int VertexCount { get; private set; }
        public int SegmentCount { get; private set; }
        public bool HasBakedVisibilitySH { get; private set; }
        public int ContentVersion { get; private set; }
        public bool IsValid =>
            StrandBuffer != null && StrandBuffer.IsValid()
            && RestVertexBuffer != null && RestVertexBuffer.IsValid()
            && SegmentIndexBuffer != null && SegmentIndexBuffer.IsValid()
            && VisibilitySHBuffer != null && VisibilitySHBuffer.IsValid();

        internal HairGroomGpuResource(HairGroomParsedData parsed, int contentVersion, string assetName)
        {
            if (parsed == null)
                throw new ArgumentNullException(nameof(parsed));

            string prefix = string.IsNullOrEmpty(assetName) ? "HairGroom" : assetName;
            StrandCount = parsed.Strands.Length;
            VertexCount = parsed.Vertices.Length;
            SegmentCount = parsed.Segments.Length;
            ContentVersion = contentVersion;

            StrandBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                StrandCount,
                HairGroomStrandDesc.Stride)
            {
                name = $"{prefix}_Strands",
            };
            StrandBuffer.SetData(parsed.Strands);

            RestVertexBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                VertexCount,
                HairGroomRestVertex.Stride)
            {
                name = $"{prefix}_RestVertices",
            };
            RestVertexBuffer.SetData(parsed.Vertices);

            SegmentIndexBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SegmentCount,
                HairGroomSegmentIndex.Stride)
            {
                name = $"{prefix}_SegmentIndices",
            };
            SegmentIndexBuffer.SetData(parsed.Segments);

            VisibilitySHBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                VertexCount,
                HairGroomVisibilitySH.Stride)
            {
                name = $"{prefix}_VisibilitySH",
            };
            if (parsed.VisibilitySH != null && parsed.VisibilitySH.Length == VertexCount)
            {
                VisibilitySHBuffer.SetData(parsed.VisibilitySH);
                HasBakedVisibilitySH = true;
            }
            else
            {
                // Default zero SH -> 0 scattering events -> unshadowed until baked in Phase S2.
                var zeros = new HairGroomVisibilitySH[VertexCount];
                VisibilitySHBuffer.SetData(zeros);
                HasBakedVisibilitySH = false;
            }
        }

        public void UploadVisibilitySH(HairGroomVisibilitySH[] coefficients)
        {
            if (coefficients == null || coefficients.Length != VertexCount)
            {
                throw new ArgumentException(
                    $"Expected {VertexCount} visibility SH entries.",
                    nameof(coefficients));
            }

            if (VisibilitySHBuffer == null || !VisibilitySHBuffer.IsValid())
                throw new ObjectDisposedException(nameof(HairGroomGpuResource));

            VisibilitySHBuffer.SetData(coefficients);
            HasBakedVisibilitySH = true;
        }

        public void Dispose()
        {
            StrandBuffer?.Dispose();
            StrandBuffer = null;
            RestVertexBuffer?.Dispose();
            RestVertexBuffer = null;
            SegmentIndexBuffer?.Dispose();
            SegmentIndexBuffer = null;
            VisibilitySHBuffer?.Dispose();
            VisibilitySHBuffer = null;
        }
    }

    /// <summary>
    /// Unified strand-based hair asset imported from .vividhair (Houdini 22 / DCC).
    /// Feeds both the realtime Strand Visibility Buffer pipeline and the DXR DOTS
    /// Reference Path Tracing path from one canonical binary payload.
    /// </summary>
    public sealed class HairGroomAsset : ScriptableObject
    {
        [SerializeField, HideInInspector]
        private byte[] m_BinaryData;

        [SerializeField] private int m_StrandCount;
        [SerializeField] private int m_VertexCount;
        [SerializeField] private int m_SegmentCount;
        [SerializeField] private Bounds m_Bounds = new Bounds(Vector3.zero, Vector3.one);
        [SerializeField] private HairGroomFlags m_Flags;
        [SerializeField] private float m_DefaultRadius = 0.0005f;
        [SerializeField] private int m_ContentVersion;

        [NonSerialized] private HairGroomGpuResource m_GpuResource;

        public int StrandCount => m_StrandCount;
        public int VertexCount => m_VertexCount;
        public int SegmentCount => m_SegmentCount;
        public Bounds Bounds => m_Bounds;
        public HairGroomFlags Flags => m_Flags;
        public float DefaultRadius => m_DefaultRadius;
        public int ContentVersion => m_ContentVersion;
        public bool HasBakedVisibilitySH => (m_Flags & HairGroomFlags.HasBakedL1SH) != 0;
        public bool HasBinaryData => m_BinaryData != null && m_BinaryData.Length >= HairGroomBinaryHeader.Stride;
        public int BinarySizeBytes => m_BinaryData?.Length ?? 0;

        public void ImportFromBinary(byte[] binaryData)
        {
            var parsed = HairGroomBinaryCodec.Read(binaryData);
            m_BinaryData = new byte[binaryData.Length];
            Buffer.BlockCopy(binaryData, 0, m_BinaryData, 0, binaryData.Length);

            m_StrandCount = (int)parsed.Header.StrandCount;
            m_VertexCount = (int)parsed.Header.VertexCount;
            m_SegmentCount = (int)parsed.Header.SegmentCount;
            m_Bounds = parsed.Header.ToUnityBounds();
            m_Flags = (HairGroomFlags)parsed.Header.Flags;
            m_DefaultRadius = parsed.Header.DefaultRadius;
            m_ContentVersion++;

            ReleaseGpuResource();
        }

        public bool TryParse(out HairGroomParsedData parsed)
        {
            parsed = null;
            if (!HasBinaryData)
                return false;

            parsed = HairGroomBinaryCodec.Read(m_BinaryData);
            return true;
        }

        /// <summary>
        /// Converts the groom segments into VividRP's 48-byte HairGpuStrandSegment ABI
        /// so the same asset can drive HairDotsGpuStream / Reference Path Tracing.
        /// </summary>
        public int BuildGpuStrandSegments(
            HairGpuStrandSegment[] destination,
            int maxSegments = 0,
            float radiusScale = 1.0f)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (float.IsNaN(radiusScale) || float.IsInfinity(radiusScale) || radiusScale <= 0.0f)
                throw new ArgumentOutOfRangeException(nameof(radiusScale));
            if (!TryParse(out var parsed))
                return 0;

            int count = parsed.Segments.Length;
            if (maxSegments > 0 && maxSegments < count)
                count = maxSegments;
            if (destination.Length < count)
            {
                throw new ArgumentException(
                    $"Destination buffer length {destination.Length} is smaller than segment count {count}.",
                    nameof(destination));
            }

            float invStrandCount = 1.0f / Mathf.Max(1, parsed.Strands.Length);
            var vertices = parsed.Vertices;
            var segments = parsed.Segments;

            for (int i = 0; i < count; i++)
            {
                ref readonly var seg = ref segments[i];
                uint v0Index = seg.VertexIndex0;
                uint v1Index = v0Index + 1u;
                ref readonly var v0 = ref vertices[v0Index];
                ref readonly var v1 = ref vertices[v1Index];

                float strandFrac = (seg.StrandIndex + 0.5f) * invStrandCount;
                float r0 = Mathf.Max(v0.Radius * radiusScale, 1e-6f);
                float r1 = Mathf.Max(v1.Radius * radiusScale, 1e-6f);

                destination[i] = new HairGpuStrandSegment
                {
                    StartPositionRadius = new Vector4(v0.PositionOS.x, v0.PositionOS.y, v0.PositionOS.z, r0),
                    EndPositionRadius = new Vector4(v1.PositionOS.x, v1.PositionOS.y, v1.PositionOS.z, r1),
                    StartEndUV = new Vector4(v0.StrandU, strandFrac, v1.StrandU, strandFrac),
                };
            }

            return count;
        }

        public HairGroomGpuResource GetOrCreateGpuResource()
        {
            if (m_GpuResource != null
                && m_GpuResource.IsValid
                && m_GpuResource.ContentVersion == m_ContentVersion)
            {
                return m_GpuResource;
            }

            ReleaseGpuResource();
            if (!TryParse(out var parsed))
                return null;

            m_GpuResource = new HairGroomGpuResource(parsed, m_ContentVersion, name);
            return m_GpuResource;
        }

        public void ReleaseGpuResource()
        {
            m_GpuResource?.Dispose();
            m_GpuResource = null;
        }

        private void OnDisable()
        {
            ReleaseGpuResource();
        }

        private void OnDestroy()
        {
            ReleaseGpuResource();
        }
    }
}
